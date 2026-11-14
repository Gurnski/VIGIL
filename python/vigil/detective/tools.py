"""The tools the AI Detective can call to look things up in the scan's results.

The model is shown only a handful of findings up front. For anything else (a place,
a variable, "the fastest", "the strongest cascade") it asks for them with these tools,
and the answer is then checked against whatever it was given (see ``grounding``).

The tools only read. They never compute a statistic: a search filters and sorts
findings that already exist, and returns them exactly as the scan wrote them. The
filters follow the contract's ``GET /findings`` (kind, variable, significant, limit,
and a latitude and longitude box), so the same tools can be pointed at the service
later by replacing ``FindingStore``.

    store = FindingStore(findings, run)
    result, is_error = run_tool(store, "search_findings", {"variable": "precipitation"})

Every tool returns JSON-ready data. A bad request comes back as an error message for
the model to read, never as an exception.
"""

from __future__ import annotations

import json

from vigil.detective.prompt import compact_finding, compact_run

DEFAULT_LIMIT = 5
MAX_LIMIT = 10
KINDS = ("trend", "change_point", "cascade", "contradiction")
DIRECTIONS = ("increasing", "decreasing")
SORTS = ("rank", "abs_slope", "p_adjusted")

TOOLS = [
    {
        "name": "search_findings",
        "description": (
            "Search the scan's findings. Returns at most 10 matching findings, in the order asked for, and how "
            "many findings match in all. Only significant findings are returned unless you ask for the others. "
            "Use it for a place (give a latitude and longitude box), a variable, a kind of finding, or to find the "
            "strongest or fastest ones (sort_by abs_slope, for one variable at a time, as slopes in different "
            "units cannot be compared)."
        ),
        "input_schema": {
            "type": "object",
            "properties": {
                "variable": {
                    "type": "string",
                    "description": "A variable id: temperature, precipitation, soil_moisture, vegetation or earthquakes. "
                    "Matches any finding that involves it, including cascades.",
                },
                "kind": {"type": "string", "enum": list(KINDS)},
                "direction": {
                    "type": "string",
                    "enum": list(DIRECTIONS),
                    "description": "Trend findings only: increasing (rising) or decreasing (falling).",
                },
                "significant": {
                    "type": "boolean",
                    "description": "Default true. Pass false to see only the findings that did not pass the threshold after correction.",
                },
                "include_not_significant": {
                    "type": "boolean",
                    "description": "Pass true to get significant findings and the ones that did not pass, together, significant first. "
                    "Use it to see everything the scan holds for a cell or a place. It replaces `significant`. "
                    "With sort_by abs_slope the ones that did not pass can come first, so leave sort_by at rank.",
                },
                "cell_id": {"type": "string", "description": "One cell, such as r60c112."},
                "lat_min": {"type": "number"},
                "lat_max": {"type": "number"},
                "lon_min": {"type": "number", "description": "Longitude runs -180 to 180. If lon_min is greater than lon_max the box crosses the dateline."},
                "lon_max": {"type": "number"},
                "sort_by": {
                    "type": "string",
                    "enum": list(SORTS),
                    "description": "rank (default, the scan's own ranking), abs_slope (largest change per decade first; "
                    "trends and contradictions only) or p_adjusted (smallest first).",
                },
                "limit": {"type": "integer", "description": f"How many to return, 1 to {MAX_LIMIT}. Default {DEFAULT_LIMIT}."},
            },
        },
    },
    {
        "name": "get_finding",
        "description": "Get one finding by its exact id, and the ids of the findings it links to.",
        "input_schema": {
            "type": "object",
            "properties": {"id": {"type": "string", "description": "A finding id, such as trend.temperature.r60c112."}},
            "required": ["id"],
        },
    },
    {
        "name": "get_run_summary",
        "description": "What the scan covered: the period, the grid, how many tests were run, skipped and significant, "
        "overall and for each variable, and how many findings there are of each kind.",
        "input_schema": {"type": "object", "properties": {}},
    },
]


class FindingStore:
    """The scan's findings and run record, with the lookups the tools need.

    ``findings`` is the contract's list of findings. Nothing is changed.
    """

    def __init__(self, findings: list[dict], run: dict | None = None):
        self.findings = list(findings)
        self.run = run
        self.by_id = {f["id"]: f for f in self.findings}

    def search(
        self,
        *,
        variable: str | None = None,
        kind: str | None = None,
        direction: str | None = None,
        significant: bool | None = True,
        cell_id: str | None = None,
        lat_min: float | None = None,
        lat_max: float | None = None,
        lon_min: float | None = None,
        lon_max: float | None = None,
        sort_by: str = "rank",
        limit: int = DEFAULT_LIMIT,
    ) -> tuple[list[dict], int]:
        """The findings that match, in order, cut to ``limit``, and the number that match in all."""
        matches = [
            f for f in self.findings
            if (significant is None or f["significant"] == significant)
            and (kind is None or f["kind"] == kind)
            and (variable is None or any(v["id"] == variable for v in f["variables"]))
            and (cell_id is None or f["cell"]["id"] == cell_id or _paired_cell(f) == cell_id)
            and (direction is None or (f["kind"] == "trend" and f["result"]["direction"] == direction))
            and _in_box(f["cell"], lat_min, lat_max, lon_min, lon_max)
        ]
        matches.sort(key=_SORT_KEYS[sort_by])
        return matches[:limit], len(matches)

    def variables(self) -> list[str]:
        """The variables of this scan: those the run lists, and any a finding involves."""
        listed = set(self.run["settings"]["variables"]) if self.run else set()
        return sorted(listed | {v["id"] for f in self.findings for v in f["variables"]})


def run_tool(store: FindingStore, name: str, arguments) -> tuple[dict, bool]:
    """Run one tool. Returns ``(result, is_error)``; never raises for a bad request."""
    try:
        if not isinstance(arguments, dict):
            return {"error": "The tool input must be an object."}, True
        if name == "search_findings":
            return _search(store, arguments), False
        if name == "get_finding":
            return _get(store, arguments), False
        if name == "get_run_summary":
            return _summary(store), False
        return {"error": f"There is no tool called {name!r}. Tools: {[t['name'] for t in TOOLS]}."}, True
    except _BadRequest as error:
        return {"error": str(error)}, True


def result_text(result: dict) -> str:
    """The tool result as the text sent back to the model."""
    return json.dumps(result, separators=(",", ":"), ensure_ascii=False)


# ------------------------------------------------------------------ the tools

class _BadRequest(Exception):
    pass


def _search(store: FindingStore, a: dict) -> dict:
    known = {"variable", "kind", "direction", "significant", "include_not_significant", "cell_id", "lat_min", "lat_max",
             "lon_min", "lon_max", "sort_by", "limit"}
    unknown = sorted(set(a) - known)
    if unknown:
        raise _BadRequest(f"Unknown search option(s): {unknown}. Options: {sorted(known)}.")
    variable = a.get("variable")
    if variable is not None and variable not in store.variables():
        raise _BadRequest(f"{variable!r} is not a variable of this scan. Variables: {store.variables()}.")
    if a.get("kind") is not None and a["kind"] not in KINDS:
        raise _BadRequest(f"kind must be one of {list(KINDS)}.")
    if a.get("direction") is not None and a["direction"] not in DIRECTIONS:
        raise _BadRequest(f"direction must be one of {list(DIRECTIONS)}.")
    sort_by = a.get("sort_by", "rank")
    if sort_by not in SORTS:
        raise _BadRequest(f"sort_by must be one of {list(SORTS)}.")
    significant = a.get("significant", True)
    if not isinstance(significant, bool):
        raise _BadRequest("significant must be true or false.")
    include = a.get("include_not_significant", False)
    if not isinstance(include, bool):
        raise _BadRequest("include_not_significant must be true or false.")
    if include:
        significant = None                      # both, whatever `significant` says
    box = {k: _number(a.get(k), k) for k in ("lat_min", "lat_max", "lon_min", "lon_max")}
    for k, low, high in (("lat_min", -90, 90), ("lat_max", -90, 90), ("lon_min", -180, 180), ("lon_max", -180, 180)):
        if box[k] is not None and not low <= box[k] <= high:
            raise _BadRequest(f"{k} must be between {low} and {high}.")
    if box["lat_min"] is not None and box["lat_max"] is not None and box["lat_min"] > box["lat_max"]:
        raise _BadRequest("lat_min must not be greater than lat_max.")
    limit = a.get("limit", DEFAULT_LIMIT)
    if isinstance(limit, bool) or not isinstance(limit, int):
        raise _BadRequest(f"limit must be a whole number from 1 to {MAX_LIMIT}.")
    limit = max(1, min(MAX_LIMIT, limit))

    found, total = store.search(
        variable=variable, kind=a.get("kind"), direction=a.get("direction"), significant=significant,
        cell_id=a.get("cell_id"), sort_by=sort_by, limit=limit, **box,
    )
    result = {
        "matching": total, "shown": len(found), "complete": total == len(found), "sorted_by": sort_by,
        "findings": [compact_finding(f) for f in found],
    }
    if total > len(found):
        result["note"] = "More findings match than are shown. Do not state how many match; say there are more."
    return result


def _get(store: FindingStore, a: dict) -> dict:
    finding_id = a.get("id")
    if not isinstance(finding_id, str) or not finding_id:
        raise _BadRequest("id is needed: the exact id of a finding.")
    finding = store.by_id.get(finding_id)
    if finding is None:
        raise _BadRequest(f"There is no finding with the id {finding_id!r}.")
    return {"finding": compact_finding(finding)}


def _summary(store: FindingStore) -> dict:
    if store.run is None:
        raise _BadRequest("No run summary is available.")
    return {"run": compact_run(store.run)}


# ------------------------------------------------------------------ filters and order

def _number(value, name: str):
    if value is None:
        return None
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise _BadRequest(f"{name} must be a number.")
    return float(value)


def _paired_cell(finding: dict) -> str | None:
    return finding["contradiction"]["paired_cell"]["id"] if finding["kind"] == "contradiction" else None


def _in_box(cell: dict, lat_min, lat_max, lon_min, lon_max) -> bool:
    """True when the cell overlaps the box. Missing edges are open. ``lon_min > lon_max`` crosses the dateline."""
    if lat_min is not None and cell["lat_max"] <= lat_min:
        return False
    if lat_max is not None and cell["lat_min"] >= lat_max:
        return False
    if lon_min is None and lon_max is None:
        return True
    low = -180.0 if lon_min is None else lon_min
    high = 180.0 if lon_max is None else lon_max
    if low <= high:
        return cell["lon_max"] > low and cell["lon_min"] < high
    return cell["lon_max"] > low or cell["lon_min"] < high


def _slope(finding: dict):
    if finding["kind"] == "trend":
        return finding["result"]["slope_per_decade"]
    if finding["kind"] == "contradiction":
        return finding["contradiction"]["slope_per_decade"]
    return None


def _p_adjusted(finding: dict):
    for block in ("result", "cascade"):
        if block in finding:
            return finding[block]["p_adjusted"]
    return None


def _by_slope(f: dict):
    slope = _slope(f)
    return (slope is None, -abs(slope) if slope is not None else 0.0, f["id"])


def _by_p(f: dict):
    p = _p_adjusted(f)
    return (p is None, p if p is not None else 0.0, f["id"])


_SORT_KEYS = {
    "rank": lambda f: (f["rank"], f["id"]),
    "abs_slope": _by_slope,
    "p_adjusted": _by_p,
}
