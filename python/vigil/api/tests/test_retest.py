"""Re-testing the scan's trend findings on a second product: the rule, and what it writes."""

from __future__ import annotations

import copy
import importlib.util
import json

import numpy as np
import pytest

from vigil.api.app import FIXTURE
from vigil.api.document import EXPLORATORY, ScanData, full_scan, write
from vigil.api.retest import OUTCOMES, SECOND_PRODUCTS, outcome_of, retest, second_trend, sentence

CONTRACT = FIXTURE.parents[1]


def made_up() -> ScanData:
    """Noise, with a warming block and a drying block planted in it."""
    rng = np.random.default_rng(3)
    rows, columns, months = 6, 8, 240
    month = np.arange(months)
    season = np.sin(2 * np.pi * month / 12)
    temperature = 280 + 5 * season + rng.normal(0, 0.6, (rows, columns, months))
    precipitation = 80 + 30 * season + rng.normal(0, 8, (rows, columns, months))
    temperature[1:4, 1:5] += 0.012 * month
    precipitation[3:5, 4:7] -= 0.09 * month
    grid = {"cell_degrees": 2.5, "rows": rows, "columns": columns, "lat_min": 10.0, "lon_min": -20.0}
    return ScanData(data={"temperature": temperature, "precipitation": precipitation}, grid=grid,
                    period_start="2004-01-01", regridding="Made up for the tests", synthetic=True)


@pytest.fixture(scope="module")
def data() -> ScanData:
    return made_up()


@pytest.fixture(scope="module")
def scanned(data) -> dict:
    return full_scan(data)


def trends(document: dict, variable: str) -> list[dict]:
    return [f for f in document["findings"] if f["kind"] == "trend" and f["variables"][0]["id"] == variable and f["significant"]]


def test_the_rule_compares_direction_and_significance_only(data):
    month = np.arange(240)
    rng = np.random.default_rng(5)
    rising = 0.02 * month + rng.normal(0, 0.5, 240)
    weak = 0.00005 * month + rng.normal(0, 0.5, 240)             # too faint to stand out from the noise
    strong = second_trend(rising, 1, 0.05, 0.95, 0.9)
    faint = second_trend(weak, 1, 0.05, 0.95, 0.9)
    assert outcome_of("increasing", strong, 0.05) == "confirmed"
    assert outcome_of("decreasing", strong, 0.05) == "opposite"
    assert faint.p_value >= 0.05 and outcome_of("increasing" if faint.slope_per_decade > 0 else "decreasing", faint, 0.05) == "same_direction"
    # The size of the second record's trend plays no part: a thousand times larger is the same answer.
    assert outcome_of("increasing", second_trend(rising * 1000, 1, 0.05, 0.95, 0.9), 0.05) == "confirmed"


def test_too_little_data_or_a_flat_series_cannot_be_re_tested():
    month = np.arange(240, dtype=float)
    thin = 0.02 * month
    thin[:40] = np.nan                                     # 83% of months, under the scan's 90%
    assert second_trend(thin, 1, 0.05, 0.95, 0.9) is None
    assert second_trend(np.full(240, 3.0), 1, 0.05, 0.95, 0.9) is None
    assert outcome_of("increasing", None, 0.05) == "not_possible"


def test_a_second_record_of_the_same_change_confirms_the_findings(data, scanned):
    document = copy.deepcopy(scanned)
    rng = np.random.default_rng(9)
    # A second record with the same planted warming and its own noise, in other units.
    second = 9.0 / 5.0 * data.data["temperature"] + rng.normal(0, 0.3, data.data["temperature"].shape)
    retest(document, {"temperature": second})

    warming = trends(document, "temperature")
    assert warming and all(f["standing"]["retest"]["outcome"] == "confirmed" for f in warming)
    for finding in warming:
        standing = finding["standing"]
        assert standing["status"] == "retested" and EXPLORATORY not in standing["limitations"]
        assert any(line.startswith("Re-tested on GISTEMP") for line in standing["limitations"])
        assert SECOND_PRODUCTS["temperature"]["in_common"] in standing["limitations"]
        block = standing["retest"]
        assert block["product"]["product"] == SECOND_PRODUCTS["temperature"]["product"]
        assert block["direction"] == finding["result"]["direction"] and block["p_value"] < 0.05
        # Nothing the scan computed has moved.
        before = next(f for f in scanned["findings"] if f["id"] == finding["id"])
        assert finding["result"] == before["result"] and finding["rank"] == before["rank"] and finding["score"] == before["score"]

    # Precipitation had no second product: untouched.
    assert all("retest" not in f["standing"] and f["standing"]["status"] == "exploratory" for f in trends(document, "precipitation"))
    line = document["run"]["retest"]["by_variable"][0]
    assert line["variable"] == "temperature" and line["confirmed"] == len(warming) == line["findings"]
    assert line["other_cells"] == 6 * 8 - len(warming) and line["other_cells_significant"] <= line["other_cells_tested"]


def test_a_second_record_that_disagrees_leaves_the_finding_exploratory(data, scanned):
    document = copy.deepcopy(scanned)
    rng = np.random.default_rng(10)
    month = np.arange(data.months)
    noise = 80 + rng.normal(0, 8, data.data["precipitation"].shape)          # no trend at all
    rising = noise + 0.09 * month                                             # the other way everywhere
    retest(document, {"precipitation": rising})
    drying = trends(document, "precipitation")
    assert drying and all(f["standing"]["retest"]["outcome"] == "opposite" for f in drying)
    assert all(f["standing"]["status"] == "exploratory" for f in drying)
    assert all(any("trends the other way" in line for line in f["standing"]["limitations"]) for f in drying)

    document = copy.deepcopy(scanned)
    gappy = noise.copy()
    gappy[:, :, :60] = np.nan
    retest(document, {"precipitation": gappy})
    drying = trends(document, "precipitation")
    assert all(f["standing"]["retest"]["outcome"] == "not_possible" for f in drying)
    assert all(f["standing"]["status"] == "exploratory" and "p_value" not in f["standing"]["retest"] for f in drying)
    counts = document["run"]["retest"]["by_variable"][0]
    assert sum(counts[outcome] for outcome in OUTCOMES) == counts["findings"] == len(drying)


def test_the_sentences_hold_no_number():
    for outcome in OUTCOMES:
        for product in SECOND_PRODUCTS.values():
            assert not any(character.isdigit() for character in sentence(outcome, "the second product")), outcome
            assert product["name"]


def test_a_re_tested_document_still_passes_the_checker(data, scanned, tmp_path):
    document = copy.deepcopy(scanned)
    retest(document, {"temperature": data.data["temperature"] + 1.0})
    path = tmp_path / "document.json"
    write(document, path)
    spec = importlib.util.spec_from_file_location("contract_check", CONTRACT / "tools" / "check.py")
    checker = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(checker)
    loaded = checker.load(path)
    checker.validate_schema(loaded)
    checker.check_results(loaded)
    assert list(checker.errors) == []
    json.dumps(document, allow_nan=False)


def test_an_array_on_another_grid_or_period_is_refused(scanned):
    with pytest.raises(ValueError, match="grid and period"):
        retest(copy.deepcopy(scanned), {"temperature": np.zeros((6, 8, 100))})
    with pytest.raises(ValueError, match="no second product"):
        retest(copy.deepcopy(scanned), {"earthquakes": np.zeros((6, 8, 240))})
