"""The AI Detective: the model looks things up with tools, and its answer is checked afterwards.

``ask`` takes a question and the scan's findings and returns an answer in the
shape the contract defines for ``POST /ask``. The model is first shown the top of
the ranking (and the finding the user has open). For anything else it calls the
tools in ``tools.py`` to search or fetch findings, up to ``MAX_TOOL_ROUNDS`` times.
Whatever it finally says is checked by ``grounding.check_answer`` against every
finding it was given or looked up, and an answer that fails the check is replaced
by a plain message, so the app never shows a claim that is not backed by a finding.

Time is limited: ``VIGIL_DETECTIVE_DEADLINE`` seconds (default ``DEADLINE_SECONDS``, 75) for the whole
answer, and each model call is given only the time that is left. If the time runs out the answer is a plain
message, never a half answer. Set the deadline below any limit in front of the service (see ``deadline_seconds``).

``use_tools=False`` goes back to a single call with the top ``MAX_FINDINGS``
findings and no tools. It is also what happens if the API refuses a request that
carries tools, so a problem there cannot take the Detective down.

The API key is read from the ``ANTHROPIC_API_KEY`` environment variable. It
lives only on the server side and is never in the repository or the C# app.
"""

from __future__ import annotations

import json
import logging
import os
import time

import anthropic

from vigil.detective.grounding import check_answer
from vigil.detective.prompt import (
    RESPONSE_SCHEMA,
    SYSTEM_PROMPT,
    build_context,
    build_user_message,
)
from vigil.detective.tools import DEFAULT_LIMIT, TOOLS, FindingStore, result_text, run_tool

log = logging.getLogger(__name__)

CONTRACT_VERSION = "0.1"
DEFAULT_MODEL = "claude-sonnet-5-5"
FALLBACK_BETA = "server-side-fallback-2026-07-01"
MAX_FINDINGS = 30          # shown up front when there are no tools
SHOWN_WITH_TOOLS = 10      # shown up front when there are tools; the model looks up the rest
MAX_TOOL_ROUNDS = 4        # rounds of tool calls before the model must answer
DEADLINE_SECONDS = 75      # the app waits 90, so stop before it gives up. Override with VIGIL_DETECTIVE_DEADLINE
MAX_TOKENS = 2000


def deadline_seconds() -> float:
    """How long ``ask`` may take, from ``VIGIL_DETECTIVE_DEADLINE`` (seconds), else ``DEADLINE_SECONDS``.

    Set it below whatever limit sits in front of the service: the function timeout (it must be
    higher than this), and a proxy or gateway if there is one. A value that is not a positive
    number is ignored.
    """
    try:
        value = float(os.environ.get("VIGIL_DETECTIVE_DEADLINE", ""))
    except ValueError:
        return float(DEADLINE_SECONDS)
    return value if value > 0 and value != float("inf") else float(DEADLINE_SECONDS)


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
    use_tools: bool = True,
) -> dict:
    """Answer one question from the findings. Returns the contract's ask response."""
    run_id = run["id"] if run else None
    shown = select_findings(findings, finding_id, SHOWN_WITH_TOOLS if use_tools else MAX_FINDINGS)
    seen = {f["id"]: f for f in shown}        # everything the model has been given or has looked up
    made: dict[str, dict] = {}                # the tool calls already made for this question
    searches: list[dict] = []                 # the searches that ran, for spotting a later one they cover
    store = FindingStore(findings, run)
    context = build_context(shown, run)

    messages = [{"role": turn["role"], "content": turn["text"]} for turn in (history or [])]
    messages.append({"role": "user", "content": build_user_message(question, context)})

    client = client or anthropic.Anthropic()
    base = {
        "model": model,
        "max_tokens": MAX_TOKENS,
        "system": SYSTEM_PROMPT,
        "output_config": {"effort": "medium", "format": {"type": "json_schema", "schema": RESPONSE_SCHEMA}},
    }
    if use_tools:
        base["tools"] = TOOLS

    deadline = deadline_seconds()
    started = time.monotonic()
    for round_number in range(MAX_TOOL_ROUNDS + 1):
        remaining = deadline - (time.monotonic() - started)
        if round_number and remaining <= 0:
            return _unavailable(run_id, model, "That took too long.")
        # Each call may only use the time that is left, so one slow reply cannot run past the deadline.
        request = {**base, "messages": messages, "timeout": max(remaining, 1.0)}
        if use_tools and round_number == MAX_TOOL_ROUNDS:
            request["tool_choice"] = {"type": "none"}     # out of rounds: it has to answer now
        try:
            if use_fallbacks:
                # If the safety classifiers decline, the API retries the same request on a fallback model.
                response = client.beta.messages.create(betas=[FALLBACK_BETA], fallbacks="default", **request)
            else:
                response = client.messages.create(**request)
        except anthropic.BadRequestError as error:
            if use_tools and round_number == 0:
                log.warning("The API refused a request with tools (%s); answering without them", error)
                return ask(question, findings, run, history, finding_id, client, model, use_fallbacks, use_tools=False)
            log.error("Detective request failed: %s", error)
            return _unavailable(run_id, model, "The assistant could not be reached.")
        except anthropic.APITimeoutError:
            log.warning("Detective request ran out of time in round %d", round_number)
            return _unavailable(run_id, model, "That took too long.")
        except anthropic.APIError as error:
            log.error("Detective request failed: %s", error)
            return _unavailable(run_id, model, "The assistant could not be reached.")

        if response.stop_reason == "tool_use" and use_tools and round_number < MAX_TOOL_ROUNDS:
            messages.append({"role": "assistant", "content": _blocks(response.content)})
            messages.append({"role": "user", "content": _run_tools(response.content, store, seen, made, searches)})
            continue
        break

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

    check = check_answer(draft, list(seen.values()), run)
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


def _blocks(content) -> list[dict]:
    """The model's reply as plain request blocks, to send back as its earlier turn."""
    blocks = []
    for block in content:
        if block.type == "text":
            blocks.append({"type": "text", "text": block.text})
        elif block.type == "tool_use":
            blocks.append({"type": "tool_use", "id": block.id, "name": block.name, "input": block.input})
    return blocks


REPEATED_CALL = (
    "You already made this exact call. Its result is earlier in this conversation: use it instead of asking again."
)
COVERED_SEARCH = (
    "You already made this search, and its results include everything this one would return. "
    "They are earlier in this conversation: use them instead of asking again."
)
ALREADY_GIVEN = (
    "This finding has already been given to you, either in the findings at the start or in an earlier tool result. "
    "Use that copy."
)


def _search_request(block):
    """A search as ``(filters, sort_by, limit)``, or ``(None, None, None)`` for any other tool call.

    ``filters`` is what the search asks for, without its order or its limit, with the default
    (significant findings only) written in so two spellings of the same search compare equal.
    ``limit`` is a whole number, or None when the call gave something else.
    """
    if block.name != "search_findings" or not isinstance(block.input, dict):
        return None, None, None
    filters = {k: v for k, v in block.input.items() if k not in ("limit", "sort_by", "include_not_significant")}
    filters.setdefault("significant", True)
    if block.input.get("include_not_significant") is True:
        filters["significant"] = "any"          # both kinds of finding: it covers a search for either
    limit = block.input.get("limit", DEFAULT_LIMIT)
    return filters, block.input.get("sort_by", "rank"), (limit if isinstance(limit, int) and not isinstance(limit, bool) else None)


def _covered(filters, sort_by, limit, searches) -> bool:
    """True when an earlier search already returned everything this one would.

    That is the case when an earlier search that returned every match asked for the same or for
    less (this one only adds filters, whatever its order or limit), or when it asked for the same
    in the same order and returned at least as many as this one wants.
    """
    for earlier in searches:
        narrower = all(k in filters and (filters[k] == v or (k == "significant" and v == "any"))
                       for k, v in earlier["filters"].items())
        if earlier["complete"] and narrower:
            return True
        if narrower and filters == earlier["filters"] and sort_by == earlier["sort_by"]                 and limit is not None and limit <= earlier["shown"]:
            return True
    return False


def _run_tools(content, store: FindingStore, seen: dict[str, dict], made: dict[str, dict], searches: list[dict]) -> list[dict]:
    """Run every tool the model asked for. Findings it is shown count as given to it.

    ``made`` remembers the calls already made for this question and ``searches`` the searches that ran.
    Asking again for the same call, for a search an earlier one already covered (see ``_covered``), or
    for a finding it already holds, is answered with a pointer to what it has instead of the same data
    a second time. Each call is logged with what was done, so the log shows the work, not the requests.
    """
    results = []
    for block in content:
        if block.type != "tool_use":
            continue
        key = f"{block.name}:{json.dumps(block.input, sort_keys=True, default=str)}"
        wanted = block.input.get("id") if isinstance(block.input, dict) else None
        wanted = wanted if isinstance(wanted, str) else None
        filters, sort_by, limit = _search_request(block)
        if key in made:
            result, is_error, how = {"note": REPEATED_CALL}, False, "repeat, not run"
        elif filters is not None and _covered(filters, sort_by, limit, searches):
            result, is_error, how = {"note": COVERED_SEARCH}, False, "covered by an earlier search, not run"
        elif block.name == "get_finding" and wanted in seen:
            result, is_error, how = {"note": ALREADY_GIVEN, "id": wanted}, False, "already given, not run"
        else:
            result, is_error = run_tool(store, block.name, block.input)
            made[key] = result
            how = "error" if is_error else "run"
            if filters is not None and not is_error:
                searches.append({"filters": filters, "sort_by": sort_by, "complete": result["complete"], "shown": result["shown"]})
        log.info("tool %s(%s): %s", block.name, json.dumps(block.input), how)
        for compact in [*result.get("findings", []), result.get("finding")]:
            if compact and compact["id"] in store.by_id:
                seen[compact["id"]] = store.by_id[compact["id"]]
        block_result = {"type": "tool_result", "tool_use_id": block.id, "content": result_text(result)}
        if is_error:
            block_result["is_error"] = True
        results.append(block_result)
    return results


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
