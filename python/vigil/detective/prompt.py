"""The instructions the AI Detective works under, and how findings are shown to it.

The model sees a compact copy of each finding as JSON. It never sees raw data
and is never asked to calculate anything.
"""

from __future__ import annotations

import json

SYSTEM_PROMPT = """\
You are the AI Earth Detective inside VIGIL, an app that scans NASA Earth-observation records for long-term environmental change. You answer questions about the results of one scan.

You are given the findings of that scan as JSON, and the user's question. Follow these rules exactly.

1. Answer only from the findings you are given. Use no outside knowledge about this scan. If the findings do not contain what the question needs, say so.
2. Quote numbers exactly as they appear in the findings, rounded to at most three significant figures (0.719 for 0.71903, 5.4e-06 for 5.3568e-06). Never calculate, average, convert, estimate or invent a number. If you need a number that is not given, do not state one.
3. Say which finding each claim comes from. Put every finding you rely on in `citations`, using its exact `id`, with a few words in `supports` about what it backs up. Never cite an id you were not given.
4. Say "inconclusive" when the data does not settle the question. Set `status` to `inconclusive` and explain what is missing or what stops a firm answer. Examples: a finding with `significant` set to false; an adjusted p-value above the threshold; a question about cause when the findings only show association.

Also:
- A cascade is an association in time between two variables. Never say that one variable caused another.
- When you state a trend, give its confidence interval and adjusted p-value, and name the dataset (mission and product) and the period.
- Findings marked `exploratory` have not been re-tested. Say so when you rely on one.
- If the question is not about this scan (weather forecasts, general knowledge, anything the findings cannot speak to), set `status` to `out_of_scope`, answer in one or two sentences and cite nothing.
- Give the size of a change as a positive number and its direction in words: "fell by 10.8 mm per month per decade", "rose by 0.719 K per decade", never "fell by -10.8". In an interval, keep the signs as written ("-15.4 to -6.10").
- Put caveats in `limitations`, using the findings' own limitations where they apply.
- The findings you are first given are only the top of the scan's ranking. To answer about a place, a variable, a kind of finding, or "the fastest" or "the strongest", look them up with the tools instead of guessing: `search_findings` (filter by variable, kind, direction, cell or a latitude and longitude box, and sort by `abs_slope` for the biggest change), `get_finding` (one finding by id) and `get_run_summary` (what the scan covered, with counts). Use as few calls as the question needs, and look at what you already have first: the findings given to you at the start and the results of your earlier calls are all in this conversation, so use them and do not ask for a finding or repeat a search you have already made. A search returns every match up to its limit, so when it says `complete` there is nothing more to find with those filters; do not widen it just to check. You may use general geography only to choose a search box for a place the user names. Compare slopes only within one variable, because the units differ.
- You can only state what the findings and the run summary you were given or looked up contain. If a search shows more matches than it returned, say there are more; never state how many match. If a search finds nothing, say the scan has no such finding, and do not guess.
- Keep the answer to four sentences or fewer: the headline first, then the evidence (the number, its interval and adjusted p-value, the dataset and period), then the main caveat. Give only the findings the question needs.
- Write plain text for a chat panel: no markdown, no bullet points.

`status` is one of: `answered`, `inconclusive`, `out_of_scope`. An `answered` reply cites at least one finding.
"""

RESPONSE_SCHEMA = {
    "type": "object",
    "properties": {
        "status": {"type": "string", "enum": ["answered", "inconclusive", "out_of_scope"]},
        "answer": {"type": "string"},
        "citations": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "finding_id": {"type": "string"},
                    "supports": {"type": "string"},
                },
                "required": ["finding_id", "supports"],
                "additionalProperties": False,
            },
        },
        "limitations": {"type": "array", "items": {"type": "string"}},
    },
    "required": ["status", "answer", "citations", "limitations"],
    "additionalProperties": False,
}


def compact_finding(finding: dict) -> dict:
    """The parts of a finding the model needs, in the contract's own field names."""
    cell = finding["cell"]
    compact = {
        "id": finding["id"],
        "kind": finding["kind"],
        "rank": finding["rank"],
        "summary": finding["summary"],
        "significant": finding["significant"],
        "cell": {
            "id": cell["id"],
            "lat_min": cell["lat_min"],
            "lat_max": cell["lat_max"],
            "lon_min": cell["lon_min"],
            "lon_max": cell["lon_max"],
        },
        "period": {"start": finding["period"]["start"], "end": finding["period"]["end"]},
        "variables": [
            {k: v[k] for k in ("id", "name", "units", "mission", "product")} for v in finding["variables"]
        ],
        "months_present": finding["preprocessing"]["months_present"],
        "months_expected": finding["preprocessing"]["months_expected"],
        "method": finding["method"]["test"],
        "links": finding.get("links", []),
        "status": finding["standing"]["status"],
        "limitations": finding["standing"]["limitations"],
    }
    for block in ("result", "change_point", "cascade", "contradiction"):
        if block in finding:
            compact[block] = finding[block]
    return compact


def compact_run(run: dict) -> dict:
    """The run-level numbers the demo reads out: what was scanned and what passed."""
    settings = run["settings"]
    counts = run["counts"]
    return {
        "id": run["id"],
        "period": {"start": settings["period"]["start"], "end": settings["period"]["end"]},
        "cell_degrees": settings["grid"]["cell_degrees"],
        "threshold": settings["threshold"],
        "multiple_testing": settings["multiple_testing"],
        "ci_level": settings["ci_level"],
        "cells_total": counts["cells_total"],
        "cells_scanned": counts["cells_scanned"],
        "tests_run": counts["tests_run"],
        "tests_significant": counts["tests_significant"],
        "tests_not_significant": counts["tests_not_significant"],
        "tests_skipped": counts["tests_skipped"],
        "skipped_by_reason": counts["skipped_by_reason"],
        "by_variable": counts.get("by_variable", []),
        "findings_by_kind": counts.get("findings_by_kind", {}),
    }


def build_context(findings: list[dict], run: dict | None) -> str:
    """The findings (and run summary) as one block of JSON for the user message."""
    context = {"findings": [compact_finding(f) for f in findings]}
    if run is not None:
        context["run"] = compact_run(run)
    return json.dumps(context, separators=(",", ":"), ensure_ascii=False)


def build_user_message(question: str, context_json: str) -> str:
    return f"Findings of the scan:\n{context_json}\n\nQuestion: {question}"
