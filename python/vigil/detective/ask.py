"""The AI Detective: one call to the language model, checked afterwards.

``ask`` takes a question and the scan's findings and returns an answer in the
shape the contract defines for ``POST /ask``. The model sees only the findings
it is given. Whatever it says is checked by ``grounding.check_answer`` before it
is returned, and an answer that fails the check is replaced by a plain message,
so the app never shows a claim that is not backed by a finding.

The API key is read from the ``ANTHROPIC_API_KEY`` environment variable. It
lives only on the server side and is never in the repository or the C# app.
"""

from __future__ import annotations

import json
import logging

import anthropic

from vigil.detective.grounding import check_answer
from vigil.detective.prompt import (
    RESPONSE_SCHEMA,
    SYSTEM_PROMPT,
    build_context,
    build_user_message,
)

log = logging.getLogger(__name__)

CONTRACT_VERSION = "0.1"
DEFAULT_MODEL = "claude-sonnet-5-5"
FALLBACK_BETA = "server-side-fallback-2026-07-01"
MAX_FINDINGS = 30
MAX_TOKENS = 2000


def select_findings(findings: list[dict], finding_id: str | None = None, limit: int = MAX_FINDINGS) -> list[dict]:
    """Pick the findings to show the model: the top ranked, plus the open one and what it links to."""
    ranked = sorted(findings, key=lambda f: f["rank"])
    chosen = ranked[:limit]
    chosen_ids = {f["id"] for f in chosen}

    if finding_id is not None:
        by_id = {f["id"]: f for f in findings}
        wanted = [finding_id]
        if finding_id in by_id:
            wanted += [link["finding_id"] for link in by_id[finding_id].get("links", [])]
        extra = [by_id[i] for i in wanted if i in by_id and i not in chosen_ids]
        chosen = sorted(chosen + extra, key=lambda f: f["rank"])
    return chosen


def ask(
    question: str,
    findings: list[dict],
    run: dict | None = None,
    history: list[dict] | None = None,
    finding_id: str | None = None,
    client=None,
    model: str = DEFAULT_MODEL,
    use_fallbacks: bool = True,
) -> dict:
    """Answer one question from the findings. Returns the contract's ask response."""
    run_id = run["id"] if run else None
    shown = select_findings(findings, finding_id)
    context = build_context(shown, run)

    messages = [{"role": turn["role"], "content": turn["text"]} for turn in (history or [])]
    messages.append({"role": "user", "content": build_user_message(question, context)})

    client = client or anthropic.Anthropic()
    request = {
        "model": model,
        "max_tokens": MAX_TOKENS,
        "system": SYSTEM_PROMPT,
        "messages": messages,
        "output_config": {"effort": "medium", "format": {"type": "json_schema", "schema": RESPONSE_SCHEMA}},
    }
    try:
        if use_fallbacks:
            # If the safety classifiers decline, the API retries the same request on a fallback model.
            response = client.beta.messages.create(betas=[FALLBACK_BETA], fallbacks="default", **request)
        else:
            response = client.messages.create(**request)
    except anthropic.APIError as error:
        log.error("Detective request failed: %s", error)
        return _unavailable(run_id, model, "The assistant could not be reached.")

    if response.stop_reason == "refusal":
        log.warning("Detective request was declined: %s", getattr(response, "stop_details", None))
        return _unavailable(run_id, model, "The assistant declined to answer that question.")
    if response.stop_reason == "max_tokens":
        return _unavailable(run_id, model, "The answer was cut off, so it is not shown.")

    try:
        text = next(block.text for block in response.content if block.type == "text")
        draft = json.loads(text)
    except (StopIteration, json.JSONDecodeError):
        log.warning("Detective reply was not valid JSON")
        return _ungrounded(run_id, model)

    required = {"status", "answer", "citations", "limitations"}
    if not isinstance(draft, dict) or not required <= draft.keys():
        log.warning("Detective reply was missing fields")
        return _ungrounded(run_id, model)

    check = check_answer(draft, shown, run)
    if not check.ok:
        log.warning("Detective answer failed the grounding check: %s", "; ".join(check.problems))
        return _ungrounded(run_id, model)

    return _response(
        run_id,
        draft["status"],
        draft["answer"],
        draft["citations"],
        draft["limitations"],
        getattr(response, "model", model),
    )


def _ungrounded(run_id: str | None, model: str) -> dict:
    return _plain(
        run_id,
        model,
        "I could not back that answer with the scan's findings, so I am not showing it. "
        "Try asking about a specific variable, place or finding.",
    )


def _unavailable(run_id: str | None, model: str, reason: str) -> dict:
    return _plain(run_id, model, f"{reason} Please try again.")


def _plain(run_id: str | None, model: str, answer: str) -> dict:
    return _response(run_id, "inconclusive", answer, [], [], model)


def _response(run_id, status, answer, citations, limitations, model) -> dict:
    response = {
        "contract_version": CONTRACT_VERSION,
        "status": status,
        "answer": answer,
        "citations": citations,
        "limitations": limitations,
        "model": model,
    }
    if run_id is not None:  # the contract allows no run_id, but not a null one
        response["run_id"] = run_id
    return response
