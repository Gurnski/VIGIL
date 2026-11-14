"""Ask the real model the contract's example questions and show what comes back.

This is the one check the tests cannot make: that the live API accepts our
request and that the answers read well. It spends a few pence of API credit.

Run from the ``python/`` folder, with the key set in this terminal only:

    $env:ANTHROPIC_API_KEY = "..."            (PowerShell)
    python -m vigil.detective.live_check

Options:
    --no-fallbacks    send a plain request, without the refusal fallback
    --model NAME      use a different model (default: claude-sonnet-5-5)
    --question TEXT   ask one question of your own instead of the four examples

Read each answer yourself. The grounding check only proves that every number and
every cited finding is in the data, not that the answer is good or complete.
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import sys
from pathlib import Path

from vigil.detective.ask import DEFAULT_MODEL, ask, select_findings
from vigil.detective.grounding import check_answer
from vigil.detective.prompt import build_context

FIXTURE = Path(__file__).resolve().parents[3] / "contract" / "fixture"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--no-fallbacks", action="store_true")
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--question")
    args = parser.parse_args()

    if not os.environ.get("ANTHROPIC_API_KEY"):
        print("ANTHROPIC_API_KEY is not set in this terminal. Set it first (see the top of this file).")
        return 2

    snapshot = json.loads((FIXTURE / "snapshot.json").read_text(encoding="utf-8"))
    findings, run = snapshot["findings"], snapshot["run"]

    if args.question:
        requests = [{"question": args.question, "history": []}]
    else:
        examples = json.loads((FIXTURE / "ask.json").read_text(encoding="utf-8"))["examples"]
        requests = [e["request"] for e in examples]

    # Show the grounding problems instead of hiding them in a log.
    logging.basicConfig(level=logging.WARNING, format="  LOG: %(message)s")

    size = len(build_context(select_findings(findings), run))
    print(f"Model {args.model}. Context sent: {size:,} characters (about {size // 4:,} tokens).\n")

    failures = 0
    for number, request in enumerate(requests, start=1):
        print(f"--- Question {number}: {request['question']}")
        response = ask(
            request["question"],
            findings,
            run,
            history=request.get("history"),
            finding_id=request.get("finding_id"),
            model=args.model,
            use_fallbacks=not args.no_fallbacks,
        )
        print(f"Status: {response['status']}")
        print(f"Answer: {response['answer']}")
        for citation in response["citations"]:
            print(f"Cites:  {citation['finding_id']}  ({citation.get('supports', '')})")
        for limitation in response["limitations"]:
            print(f"Limit:  {limitation}")

        # Re-run the check here so a replaced answer is obvious.
        replaced = response["answer"].startswith(("I could not back", "The assistant", "The answer was cut"))
        grounded = check_answer(response, select_findings(findings, request.get("finding_id")), run)
        if replaced:
            failures += 1
            print("RESULT: the answer was replaced (see the LOG line above for why)\n")
        else:
            print(f"RESULT: grounded = {grounded.ok}\n")
            failures += 0 if grounded.ok else 1

    print(f"{len(requests) - failures} of {len(requests)} answers were shown and grounded.")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
