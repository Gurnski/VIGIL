"""Checks the Detective's lookup tools on the contract's fixture, without any model.

Run from the ``python/`` folder:  python -m pytest vigil/detective/tests/test_tools.py -v
"""

import json
from pathlib import Path

import pytest

from vigil.detective.tools import MAX_LIMIT, TOOLS, FindingStore, _in_box, result_text, run_tool

FIXTURE = Path(__file__).resolve().parents[4] / "contract" / "fixture"


@pytest.fixture(scope="module")
def snapshot():
    return json.loads((FIXTURE / "snapshot.json").read_text(encoding="utf-8"))


@pytest.fixture(scope="module")
def store(snapshot):
    return FindingStore(snapshot["findings"], snapshot["run"])


def search(store, **arguments):
    result, is_error = run_tool(store, "search_findings", arguments)
    assert not is_error, result
    return result


def ids(result):
    return [f["id"] for f in result["findings"]]


# ------------------------------------------------------------------ searching

def test_the_default_search_is_significant_findings_in_rank_order(store, snapshot):
    result = search(store, limit=10)
    expected = [f for f in sorted(snapshot["findings"], key=lambda f: f["rank"]) if f["significant"]]
    assert ids(result) == [f["id"] for f in expected][:10]
    assert result["matching"] == len(expected)
    assert result["sorted_by"] == "rank"


def test_a_variable_matches_cascades_that_involve_it(store):
    result = search(store, variable="soil_moisture", limit=10)
    kinds = {f["kind"] for f in result["findings"]}
    assert "cascade" in kinds
    assert all(any(v["id"] == "soil_moisture" for v in f["variables"]) for f in result["findings"])


def test_filters_by_kind_and_direction(store):
    cascades = search(store, kind="cascade")
    assert cascades["findings"] and all(f["kind"] == "cascade" for f in cascades["findings"])
    falling = search(store, kind="trend", direction="decreasing", limit=10)
    assert falling["findings"] and all(f["result"]["direction"] == "decreasing" for f in falling["findings"])
    # direction only means something for trends
    assert all(f["kind"] == "trend" for f in search(store, direction="increasing", limit=10)["findings"])


def test_findings_that_did_not_pass_are_only_returned_when_asked_for(store):
    default = search(store, cell_id="r51c032")
    assert default["findings"] == [] and default["matching"] == 0
    asked = search(store, cell_id="r51c032", significant=False)
    assert ids(asked) == ["trend.temperature.r51c032"]
    assert asked["findings"][0]["significant"] is False


def test_a_cell_search_finds_the_paired_cell_of_a_contradiction(store):
    assert "contradiction.vegetation.r41c073-r41c074" in ids(search(store, cell_id="r41c074"))
    assert "contradiction.vegetation.r41c073-r41c074" in ids(search(store, cell_id="r41c073"))


def test_a_box_returns_findings_whose_cell_overlaps_it(store, snapshot):
    cell = next(f["cell"] for f in snapshot["findings"] if f["id"] == "trend.temperature.r60c112")
    result = search(store, lat_min=cell["lat_min"] + 0.1, lat_max=cell["lat_max"] - 0.1,
                    lon_min=cell["lon_min"] + 0.1, lon_max=cell["lon_max"] - 0.1, limit=10)
    assert "trend.temperature.r60c112" in ids(result)
    assert all(f["cell"]["id"] == "r60c112" for f in result["findings"])
    # A box that only touches the cell's edge does not overlap it.
    touching = search(store, lat_min=cell["lat_max"], lat_max=cell["lat_max"] + 1, limit=10)
    assert all(f["cell"]["id"] != "r60c112" for f in touching["findings"])


def test_a_box_can_cross_the_dateline():
    east = {"lat_min": 0, "lat_max": 2.5, "lon_min": 175.0, "lon_max": 177.5}
    west = {"lat_min": 0, "lat_max": 2.5, "lon_min": -177.5, "lon_max": -175.0}
    middle = {"lat_min": 0, "lat_max": 2.5, "lon_min": 0.0, "lon_max": 2.5}
    assert _in_box(east, None, None, 170, -170) and _in_box(west, None, None, 170, -170)
    assert not _in_box(middle, None, None, 170, -170)
    assert not _in_box(east, None, None, -170, 170)       # the same numbers the other way round are the wide box
    assert _in_box(middle, None, None, -170, 170)


def test_sorting_by_slope_puts_the_largest_change_first_and_is_not_the_rank_order(store):
    result = search(store, kind="trend", sort_by="abs_slope", limit=10)
    slopes = [abs(f["result"]["slope_per_decade"]) for f in result["findings"]]
    assert slopes == sorted(slopes, reverse=True) and len(slopes) > 1
    by_p = search(store, kind="trend", sort_by="p_adjusted", limit=10)
    p = [f["result"]["p_adjusted"] for f in by_p["findings"]]
    assert p == sorted(p)
    # Findings with no slope go last rather than failing.
    mixed = search(store, sort_by="abs_slope", limit=10)
    kinds = [f["kind"] for f in mixed["findings"]]
    assert kinds.index("cascade") > max(i for i, k in enumerate(kinds) if k in ("trend", "contradiction"))


def test_the_limit_is_clamped_and_more_matches_are_flagged(store):
    assert len(search(store, limit=1)["findings"]) == 1
    assert len(search(store, limit=500)["findings"]) <= MAX_LIMIT
    assert len(search(store, limit=0)["findings"]) == 1
    capped = search(store, limit=2)
    assert capped["matching"] > capped["shown"] == 2
    assert "More findings match" in capped["note"]
    assert "note" not in search(store, cell_id="r60c112")
    assert capped["complete"] is False
    complete = search(store, cell_id="r60c112")
    assert complete["complete"] is True and complete["matching"] == complete["shown"]


def test_a_search_that_finds_nothing_says_so(store):
    result = search(store, kind="contradiction", variable="temperature")
    assert result["matching"] == 0 and result["findings"] == []


def test_findings_come_back_exactly_as_the_scan_wrote_them(store, snapshot):
    from vigil.detective.prompt import compact_finding

    by_id = {f["id"]: f for f in snapshot["findings"]}
    for compact in search(store, limit=10)["findings"]:
        assert compact == compact_finding(by_id[compact["id"]])


# ------------------------------------------------------------------ the other tools

def test_get_finding_returns_one_finding(store):
    result, is_error = run_tool(store, "get_finding", {"id": "trend.temperature.r60c112"})
    assert not is_error and result["finding"]["id"] == "trend.temperature.r60c112"
    assert "links" in result["finding"]


def test_get_finding_for_an_unknown_id_is_an_error_the_model_can_read(store):
    result, is_error = run_tool(store, "get_finding", {"id": "trend.temperature.r00c000"})
    assert is_error and "no finding" in result["error"]
    assert run_tool(store, "get_finding", {})[1]


def test_run_summary_has_the_counts_the_demo_reads_out(store, snapshot):
    result, is_error = run_tool(store, "get_run_summary", {})
    assert not is_error
    run = result["run"]
    assert run["tests_run"] == snapshot["run"]["counts"]["tests_run"]
    assert [v["variable"] for v in run["by_variable"]] == snapshot["run"]["settings"]["variables"]
    assert run["findings_by_kind"] == snapshot["run"]["counts"]["findings_by_kind"]
    assert run_tool(FindingStore(snapshot["findings"], None), "get_run_summary", {})[1]


# ------------------------------------------------------------------ bad requests

@pytest.mark.parametrize("arguments", [
    {"variable": "methane"},
    {"kind": "earthquake"},
    {"direction": "up"},
    {"sort_by": "size"},
    {"significant": "yes"},
    {"lat_min": "north"},
    {"lat_min": 95},
    {"lon_max": 200},
    {"lat_min": 10, "lat_max": 5},
    {"limit": "ten"},
    {"limit": True},
    {"colour": "red"},
])
def test_bad_search_requests_come_back_as_errors_not_exceptions(store, arguments):
    result, is_error = run_tool(store, "search_findings", arguments)
    assert is_error and isinstance(result["error"], str)


def test_unknown_tools_and_bad_input_shapes_are_errors(store):
    assert run_tool(store, "delete_everything", {})[1]
    assert run_tool(store, "search_findings", "temperature")[1]
    assert run_tool(store, "search_findings", None)[1]


def test_the_variable_error_lists_the_variables_that_exist(store):
    result, _ = run_tool(store, "search_findings", {"variable": "methane"})
    assert "temperature" in result["error"] and "vegetation" in result["error"]


# ------------------------------------------------------------------ definitions and output

def test_the_tool_definitions_are_valid_and_match_what_run_tool_accepts(store):
    assert [t["name"] for t in TOOLS] == ["search_findings", "get_finding", "get_run_summary"]
    for tool in TOOLS:
        assert tool["description"] and tool["input_schema"]["type"] == "object"
    declared = set(TOOLS[0]["input_schema"]["properties"])
    result, is_error = run_tool(store, "search_findings", {name: None for name in declared} | {"limit": 3})
    # None means "not given" for every option, so a model that sends them all gets no error from the names.
    assert not is_error or "must be" in result["error"]


def test_results_are_plain_json_with_no_nan(store):
    for name, arguments in (("search_findings", {"limit": 10, "significant": False}), ("get_finding", {"id": "trend.temperature.r60c112"}),
                            ("get_run_summary", {})):
        result, _ = run_tool(store, name, arguments)
        json.loads(result_text(result), parse_constant=lambda c: pytest.fail(f"{c} in output"))
