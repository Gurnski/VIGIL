"""Checks the scan entry point on a small made-up grid.

Run from the ``python/`` folder:  python -m pytest vigil/analysis/tests/test_scan.py -v
"""

import json

import numpy as np
import pytest

from vigil.analysis.score import rank_findings
from vigil.analysis.scan import cell_id, cell_object, scan_trends
from vigil.analysis.synthetic import make_series

GRID = {"cell_degrees": 2.5, "lat_min": -90.0, "lon_min": -180.0}
ROWS, COLUMNS, MONTHS = 4, 5, 240
VARIABLES = ("temperature", "precipitation", "soil_moisture", "vegetation")


def make_data(seed=0, trend_cell=(2, 3), trend_variable="temperature", trend=1.5):
    """Flat noisy cells everywhere, with one strong trend put in one cell of one variable."""
    rng = np.random.default_rng(seed)
    data = {}
    for variable in VARIABLES:
        grid = np.empty((ROWS, COLUMNS, MONTHS))
        for r in range(ROWS):
            for c in range(COLUMNS):
                slope = trend if (variable == trend_variable and (r, c) == trend_cell) else 0.0
                grid[r, c] = make_series(MONTHS, trend_per_decade=slope, noise_sd=1.0, ar1=0.3,
                                         seasonal_amplitude=4.0, baseline=10.0, rng=rng)
        data[variable] = grid
    return data


def run(data, **overrides):
    kwargs = dict(start_month=1, grid=GRID, min_coverage=0.9, threshold=0.05, ci_level=0.95)
    kwargs.update(overrides)
    return scan_trends(data, **kwargs)


@pytest.fixture(scope="module")
def scanned():
    data = make_data()
    mask = np.full((ROWS, COLUMNS), None, dtype=object)
    mask[0, 0] = "ocean"
    mask[3, 4] = "ice"
    data["precipitation"][1, 1, :] = np.where(np.arange(MONTHS) % 4 == 0, data["precipitation"][1, 1, :], np.nan)  # 25% present
    return data, mask, run(data, mask=mask, rank_cap={"top_n": 20, "max_share": 0.25})


# ------------------------------------------------------------------ finding the trend

def test_inserted_trend_is_found_and_ranked_first(scanned):
    _, _, result = scanned
    assert result.findings, "the inserted trend was not found"
    top = result.findings[0]
    assert top["id"] == f"trend.temperature.{cell_id(2, 3)}"
    assert top["rank"] == 1
    assert top["significant"] is True
    assert top["result"]["direction"] == "increasing"
    assert top["result"]["slope_per_decade"] == pytest.approx(1.5, abs=0.4)
    assert top["result"]["ci_low"] < 1.5 < top["result"]["ci_high"]


def test_flat_cells_are_not_significant_after_correction(scanned):
    _, _, result = scanned
    assert [f["id"] for f in result.findings] == [f"trend.temperature.{cell_id(2, 3)}"]
    assert result.counts["tests_significant"] == 1
    # Every flat cell was still tested, and its adjusted p stays above the threshold.
    for layer in result.grids:
        for r in range(ROWS):
            for c in range(COLUMNS):
                p = layer["p_adjusted"][r][c]
                if p is not None and not (layer["variable"] == "temperature" and (r, c) == (2, 3)):
                    assert p >= 0.05


def test_finding_has_the_contract_fields_the_analysis_owns(scanned):
    _, _, result = scanned
    f = result.findings[0]
    assert f["kind"] == "trend"
    assert f["cell"] == {"id": "r02c003", "row": 2, "column": 3,
                         "lat_min": -85.0, "lat_max": -82.5, "lon_min": -172.5, "lon_max": -170.0}
    assert f["variables"][0]["id"] == "temperature"
    assert f["variables"][0]["units"] == "K"
    assert f["preprocessing"] == {"months_present": MONTHS, "months_expected": MONTHS, "coverage": 1.0}
    assert f["method"]["test"] and isinstance(f["method"]["parameters"], dict)
    assert all(isinstance(v, (str, int, float, bool)) for v in f["method"]["parameters"].values())
    assert f["result"]["units"] == "K per decade"
    assert f["result"]["p_adjusted"] >= f["result"]["p_value"]
    assert set(f["score"]) == {"total", "effect_size", "certainty", "coverage", "robustness", "cross_system"}


# ------------------------------------------------------------------ skipping

def test_masked_and_thin_cells_are_skipped_with_the_right_reason(scanned):
    _, _, result = scanned
    by = {g["variable"]: g for g in result.grids}
    for variable in VARIABLES:
        assert by[variable]["skip_reason"][0][0] == "ocean"
        assert by[variable]["skip_reason"][3][4] == "ice"
        assert by[variable]["slope_per_decade"][0][0] is None
        assert by[variable]["p_adjusted"][3][4] is None
    # Only precipitation was thinned, in cell (1, 1).
    assert by["precipitation"]["skip_reason"][1][1] == "coverage"
    assert by["temperature"]["skip_reason"][1][1] is None
    assert by["precipitation"]["slope_per_decade"][1][1] is None


def test_cell_exactly_at_min_coverage_is_tested_and_just_under_is_skipped():
    data = make_data()
    series = data["temperature"]
    series[1, 1, : int(MONTHS * 0.1)] = np.nan            # exactly 90% present
    series[1, 2, : int(MONTHS * 0.1) + 1] = np.nan        # a little under
    result = run(data)
    layer = result.grids[0]
    assert layer["skip_reason"][1][1] is None
    assert layer["skip_reason"][1][2] == "coverage"


def test_per_variable_mask_and_unmasked_variables():
    data = make_data()
    mask = {"precipitation": np.full((ROWS, COLUMNS), "ocean", dtype=object)}
    result = run(data, mask=mask)
    by = {v["variable"]: v for v in result.counts["by_variable"]}
    assert by["precipitation"]["tests_run"] == 0
    assert by["precipitation"]["tests_skipped"] == ROWS * COLUMNS
    assert by["temperature"]["tests_run"] == ROWS * COLUMNS
    assert result.counts["skipped_by_reason"]["ocean"] == ROWS * COLUMNS


def test_a_masked_cell_is_skipped_even_if_the_data_hold_a_huge_trend():
    data = make_data(trend_cell=(0, 0))
    mask = np.full((ROWS, COLUMNS), None, dtype=object)
    mask[0, 0] = "ice"
    result = run(data, mask=mask)
    assert all(f["cell"]["id"] != "r00c000" for f in result.findings)
    assert result.grids[0]["skip_reason"][0][0] == "ice"


# ------------------------------------------------------------------ counts and output

def test_counts_add_up(scanned):
    _, _, result = scanned
    c = result.counts
    assert c["tests_total"] == ROWS * COLUMNS * len(VARIABLES)
    assert c["tests_run"] + c["tests_skipped"] == c["tests_total"]
    assert c["tests_significant"] + c["tests_not_significant"] == c["tests_run"]
    assert sum(c["skipped_by_reason"].values()) == c["tests_skipped"]
    assert set(c["skipped_by_reason"]) == {"ocean", "ice", "coverage"}
    assert c["skipped_by_reason"] == {"ocean": 4, "ice": 4, "coverage": 1}
    assert [v["variable"] for v in c["by_variable"]] == list(VARIABLES)
    for v in c["by_variable"]:
        assert v["tests_significant"] + v["tests_not_significant"] == v["tests_run"]
        assert v["tests_run"] + v["tests_skipped"] == ROWS * COLUMNS
    assert sum(v["tests_run"] for v in c["by_variable"]) == c["tests_run"]
    assert c["cells_total"] == ROWS * COLUMNS
    assert c["cells_scanned"] == ROWS * COLUMNS - 2   # (0,0) and (3,4) are skipped for every variable
    assert len(result.findings) == c["tests_significant"]


def test_every_cell_is_scanned_or_skipped_never_both_nor_neither(scanned):
    _, _, result = scanned
    for layer in result.grids:
        assert layer["rows"] == ROWS and layer["columns"] == COLUMNS
        for r in range(ROWS):
            for c in range(COLUMNS):
                slope, p, why = layer["slope_per_decade"][r][c], layer["p_adjusted"][r][c], layer["skip_reason"][r][c]
                assert (why is None) == (slope is not None) == (p is not None)


def test_layer_geometry_follows_the_contract(scanned):
    _, _, result = scanned
    g = result.grids[0]
    assert (g["lat_min"], g["lat_max"], g["lon_min"], g["lon_max"]) == (-90.0, -80.0, -180.0, -167.5)
    assert g["units"] == "K per decade"
    assert [layer["units"] for layer in result.grids] == [
        "K per decade", "mm per month per decade", "kg per m2 per decade", "NDVI per decade"]


def test_no_nan_reaches_the_output(scanned):
    _, _, result = scanned
    json.dumps({"grids": result.grids, "counts": result.counts, "findings": result.findings}, allow_nan=False)


def test_findings_pass_rank_findings_again(scanned):
    _, _, result = scanned
    again = rank_findings(result.findings, result.evidence, diversify=False)
    assert [f["id"] for f in again] == [f["id"] for f in result.findings]


# ------------------------------------------------------------------ ranking and the cap

def many_trends_data():
    """Temperature trends everywhere, a weaker precipitation trend in one cell."""
    rng = np.random.default_rng(3)
    data = {v: np.stack([[make_series(MONTHS, noise_sd=1.0, rng=rng) for _ in range(COLUMNS)] for _ in range(ROWS)])
            for v in ("temperature", "precipitation")}
    for r in range(ROWS):
        for c in range(COLUMNS):
            data["temperature"][r, c] += 2.0 * np.arange(MONTHS) / 120.0
    data["precipitation"][1, 1] += 1.2 * np.arange(MONTHS) / 120.0
    return data


def test_rank_cap_limits_one_variable_and_none_means_no_cap():
    data = many_trends_data()
    capped = run(data, rank_cap={"top_n": 4, "max_share": 0.25})
    uncapped = run(data, rank_cap=None)
    assert len(capped.findings) == len(uncapped.findings) > 4
    def position(result, variable):
        return next(f["rank"] for f in result.findings if f["variables"][0]["id"] == variable)
    # The one precipitation trend is weaker than the strongest temperature trends, so without
    # a cap it comes below second place. With the cap, temperature may hold only 1 of the
    # first 4 places while precipitation has a finding to fill the next one.
    assert position(uncapped, "precipitation") > 2
    assert position(capped, "precipitation") == 2
    assert capped.findings[0]["variables"][0]["id"] == "temperature"
    assert [f["rank"] for f in capped.findings] == list(range(1, len(capped.findings) + 1))
    # Without a cap the order is plain score order.
    totals = [f["score"]["total"] for f in uncapped.findings]
    assert totals == sorted(totals, reverse=True)


def test_findings_are_not_changed_by_ranking_inputs_other_than_order():
    data = many_trends_data()
    a = run(data, rank_cap=None)
    b = run(data, rank_cap={"top_n": 4, "max_share": 0.25})
    assert {f["id"]: f["score"] for f in a.findings} == {f["id"]: f["score"] for f in b.findings}


# ------------------------------------------------------------------ awkward series

def test_flat_series_such_as_a_dry_desert_is_tested_with_p_one_and_does_not_crash():
    data = make_data()
    data["precipitation"][2, 2, :] = 0.0                                # no rain at all
    data["precipitation"][2, 1, :] = 0.1 + np.tile(np.arange(12.0), MONTHS // 12)   # same every year
    result = run(data)
    layer = next(g for g in result.grids if g["variable"] == "precipitation")
    for column in (1, 2):
        assert layer["skip_reason"][2][column] is None
        assert layer["slope_per_decade"][2][column] == 0.0
        assert layer["p_adjusted"][2][column] == 1.0
    assert result.flat_tests >= 2
    assert result.counts["tests_run"] + result.counts["tests_skipped"] == result.counts["tests_total"]


def test_gaps_in_the_series_are_counted_in_months_present():
    data = make_data()
    data["temperature"][2, 3, ::10] = np.nan                            # 24 of 240 months missing
    result = run(data)
    f = next(f for f in result.findings if f["id"] == "trend.temperature.r02c003")
    assert f["preprocessing"]["months_present"] == 216
    assert f["preprocessing"]["months_expected"] == 240
    assert f["preprocessing"]["coverage"] == pytest.approx(0.9)


# ------------------------------------------------------------------ bad input

def test_bad_input_is_refused_with_a_clear_message():
    data = make_data()
    with pytest.raises(ValueError, match="shape"):
        run({"temperature": data["temperature"][0]})
    with pytest.raises(ValueError, match="infinite"):
        bad = {"temperature": data["temperature"].copy()}
        bad["temperature"][0, 0, 0] = np.inf
        run(bad)
    with pytest.raises(ValueError, match="same shape"):
        run({"temperature": data["temperature"], "precipitation": data["precipitation"][:, :, :100]})
    with pytest.raises(ValueError, match="grid rows"):
        run(data, grid={**GRID, "rows": 99})
    with pytest.raises(ValueError, match="mask"):
        run(data, mask=np.full((ROWS, COLUMNS), "sea", dtype=object))
    with pytest.raises(ValueError, match="mask has shape"):
        run(data, mask=np.full((2, 2), None, dtype=object))
    with pytest.raises(ValueError, match="start_month"):
        run(data, start_month=13)
    with pytest.raises(ValueError, match="units"):
        run({"methane": data["temperature"]})


def test_progress_is_reported():
    calls = []
    run(make_data(), progress=lambda done, total: calls.append((done, total)))
    assert calls and calls[-1] == (ROWS * COLUMNS * len(VARIABLES), ROWS * COLUMNS * len(VARIABLES))


def test_cell_helpers_match_the_contract_examples():
    assert cell_id(60, 112) == "r60c112"
    cell = cell_object(60, 112, GRID)
    assert (cell["lat_min"], cell["lat_max"], cell["lon_min"], cell["lon_max"]) == (60.0, 62.5, 100.0, 102.5)
