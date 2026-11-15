"""Re-tests the scan's trend findings on a second product for the same variable.

    python -m vigil.api.retest snapshot.json ../data/scan/retest snapshot-retested.json

The brief asks that discovery and validation be kept apart: a pattern the scan finds should
be tested again on another measure of the same thing, and labelled exploratory when it has
not been. This is that second step. It reads a stored scan, runs the scan's own trend test
on a second product in every cell the scan tested, and writes the document back with what
it found. It changes no statistic of the scan: slopes, p-values, ranks and scores stay as
they were. Only each trend finding's ``standing`` changes, and the run record gains a
``retest`` summary. The format is in ``contract/README.md`` under "Re-testing".

It computes no statistics of its own: the test is ``vigil.analysis.trend.trend_test`` on
anomalies from ``deseasonalise``, exactly as the scan runs it on a cell.

The rule, fixed before any result was seen
------------------------------------------
For a significant trend finding, the second product's series for the same cell is tested
over the scan's period, as far as that product covers it: Aqua's record starts in July 2002,
so its tests run on the months from then, and a cell where it has under the scan's share of
months cannot be re-tested. Only the direction and the significance of its trend are
compared, never the size, because a second product may be in other units.

The rule and the second products for temperature, precipitation, soil moisture and vegetation
were written before any re-test had run. The second product for sea surface temperature was
chosen later, after that variable's scan results had been seen, because the file was already
to hand. The rule it is tested by is the same one.

``confirmed``       its Sen slope has the same sign as the finding's, and its own p-value
                    is below the scan's threshold. Only this sets ``standing.status`` to
                    ``retested``.
``same_direction``  the same sign, but not significant on its own.
``opposite``        the other sign, significant or not.
``not_possible``    the second product has too little data in the cell, or a flat series.

One test is made per finding and it was fixed in advance, so its p-value is not corrected
for many tests. By chance alone about half the threshold's share of findings would be
confirmed (the right sign and below the threshold), so a confirmation is evidence, not proof.

How much a confirmation is worth depends on how different the second product is. None of
them is fully independent of the product the scan used, and ``SECOND_PRODUCTS`` says for
each one what the two have in common. The summary also counts, among the cells where the
scan found nothing, how often the second product has a significant trend of its own: the
rate to hold the confirmations against.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import numpy as np

from vigil.analysis.trend import deseasonalise, trend_test
from vigil.api.document import EXPLORATORY, month_dates, write

#: The second product for each scan variable: where it comes from, and what it shares with the first.
SECOND_PRODUCTS = {
    "temperature": {
        "name": "GISTEMP",
        "mission": "NASA GISS GISTEMP v4", "product": "gistemp1200_GHCNv4_ERSSTv5", "field": "tempanomaly",
        "units": "K", "source_url": "https://data.giss.nasa.gov/gistemp/",
        "native_resolution": "2 degrees, monthly, smoothed over 1,200 km",
        "in_common": "GISTEMP is built from thermometers at weather stations and on ships, and MERRA-2 is a weather model "
                     "fitted to observations, so the two are made in different ways. GISTEMP is smoothed over a far "
                     "larger area than one cell, so it cannot confirm a change that is local to the cell.",
    },
    "precipitation": {
        "name": "GPCP",
        "mission": "GPCP v3.3", "product": "GPCPMON", "field": "sat_gauge_precip",
        "units": "mm per month", "source_url": "https://search.earthdata.nasa.gov/search?q=GPCPMON",
        "native_resolution": "0.5 degrees, monthly",
        "in_common": "GPCP and IMERG are both satellite estimates adjusted to rain gauges, and they share gauge data and "
                     "some satellite inputs. Agreement between them is weaker evidence than agreement with a record made another way.",
    },
    "soil_moisture": {
        "name": "MERRA-2 land",
        "mission": "MERRA-2", "product": "M2TMNXLND", "field": "GWETTOP",
        "units": "share of saturation", "source_url": "https://search.earthdata.nasa.gov/search?q=M2TMNXLND",
        "native_resolution": "0.5 by 0.625 degrees, monthly",
        "in_common": "Both are land-surface models driven by weather analyses and corrected rainfall. They use different "
                     "models and different inputs, but neither measures soil moisture directly.",
    },
    "sea_surface_temperature": {
        "name": "GISTEMP",
        # Over the sea GISTEMP is ERSST, so the array is the one fetched for air temperature.
        "array": "temperature",
        "mission": "NASA GISS GISTEMP v4 (ERSST v5 over the sea)", "product": "gistemp1200_GHCNv4_ERSSTv5", "field": "tempanomaly",
        "units": "K", "source_url": "https://data.giss.nasa.gov/gistemp/",
        "native_resolution": "2 degrees, monthly, smoothed over 1,200 km",
        "in_common": "Over the sea GISTEMP is the ERSST analysis, built from ships, buoys and floats and not from satellites, "
                     "so the two are made in different ways. It is smoothed over a far larger area than one cell, so it "
                     "cannot confirm a change that is local to the cell.",
    },
    "vegetation": {
        "name": "Aqua MODIS",
        "mission": "Aqua MODIS", "product": "MYD13C2", "field": "CMG 0.05 Deg Monthly NDVI",
        "units": "NDVI", "source_url": "https://search.earthdata.nasa.gov/search?q=MYD13C2",
        "native_resolution": "0.05 degrees, monthly",
        "in_common": "Aqua carries the same kind of instrument as Terra and its NDVI is made by the same method, so it is "
                     "a second sensor, not a second method. Its record starts in July 2002. Both satellites have drifted "
                     "from their original orbits in recent years, which can affect the last years of both records.",
    },
}

OUTCOMES = ("confirmed", "same_direction", "opposite", "not_possible")

RULE = ("Where a second product exists for a variable, each of its significant trend findings is tested again on it: "
        "in the same cell, over the months of the scan's period that the second product covers, with the scan's own "
        "trend test. It is confirmed when the second record's trend has the same sign and is significant on its own at "
        "the scan's threshold. One test per finding, not corrected for many tests. Variables with no second product "
        "are not re-tested, and their findings stay exploratory.")

#: A series whose anomalies vary by less than this share of its largest value is flat: nothing to test.
FLAT_TOLERANCE = 1e-10


def second_trend(series: np.ndarray, start_month: int, threshold: float, ci_level: float, min_coverage: float):
    """The scan's trend test on one series of a second product, or ``None`` when it cannot be run.

    ``None`` when too few months are present (the scan's own coverage rule) or the series is flat.
    """
    present = int(np.isfinite(series).sum())
    if present < 10 or present / series.size < min_coverage:
        return None
    anomalies = deseasonalise(series, start_month)
    finite = anomalies[np.isfinite(anomalies)]
    scale = float(np.max(np.abs(series[np.isfinite(series)])))
    if float(np.ptp(finite)) <= FLAT_TOLERANCE * max(scale, 1e-300):
        return None
    result = trend_test(anomalies, alpha=threshold, ci_level=ci_level)
    if not (np.isfinite(result.p_value) and np.isfinite(result.slope_per_decade)) or result.slope_per_decade == 0:
        return None
    return result


def outcome_of(direction: str, result, threshold: float) -> str:
    """One of ``OUTCOMES``, by the rule in the module docstring. ``direction`` is the finding's own."""
    if result is None:
        return "not_possible"
    same_sign = (result.slope_per_decade > 0) == (direction == "increasing")
    if not same_sign:
        return "opposite"
    return "confirmed" if result.p_value < threshold else "same_direction"


def sentence(outcome: str, name: str) -> str:
    """What the re-test found, for ``standing.limitations``. It holds no number, so nothing in it can be misquoted."""
    return {
        "confirmed": f"Re-tested on {name}: the second record has a significant trend in the same direction.",
        "same_direction": f"Re-tested on {name}: the second record leans the same way, but its trend is not significant "
                          "on its own, so the finding stays exploratory.",
        "opposite": f"Re-tested on {name}: the second record trends the other way. Treat this finding with caution.",
        "not_possible": f"It could not be re-tested on {name}, which has too little data in this cell, so the finding stays exploratory.",
    }[outcome]


def retest(document: dict, second: dict[str, np.ndarray]) -> dict:
    """Adds the re-tests to a stored scan, in place, and returns it.

    ``second[variable]`` is the second product's array on the scan grid, ``(rows, columns, months)``,
    for the scan's whole period. A variable with no array is left as it is.
    """
    run = document.get("run")
    if run is None or run["settings"].get("region") is not None:
        raise ValueError("re-testing needs the stored scan of the whole grid, with its run record")
    settings = run["settings"]
    grid = settings["grid"]
    threshold, ci_level, min_coverage = settings["threshold"], settings["ci_level"], settings["min_coverage"]
    start = settings["period"]["start"]
    start_month = int(start[5:7])
    layers = {layer["variable"]: layer for layer in document.get("grids", [])}

    summary = []
    for variable, array in second.items():
        if variable not in SECOND_PRODUCTS:
            raise ValueError(f"no second product is recorded for the variable {variable!r}")
        if variable not in layers:
            continue
        months = array.shape[2] if array.ndim == 3 else -1
        dates = month_dates(start, months) if months > 0 else []
        if array.shape[:2] != (grid["rows"], grid["columns"]) or not dates or dates[-1] != settings["period"]["end"]:
            raise ValueError(f"{variable}: the second product's array is {array.shape}, which is not the scan's grid and period")
        product = SECOND_PRODUCTS[variable]
        described = {key: product[key] for key in ("mission", "product", "field", "units", "source_url", "native_resolution")}

        # Every cell the scan tested gets the test once, whether or not the scan found anything there.
        tested = np.array(layers[variable]["skip_reason"], dtype=object) == None      # noqa: E711
        results = {(int(row), int(column)): second_trend(array[row, column], start_month, threshold, ci_level, min_coverage)
                   for row, column in zip(*np.nonzero(tested))}

        counts = dict.fromkeys(OUTCOMES, 0)
        found: set[tuple[int, int]] = set()
        for finding in document["findings"]:
            if finding["kind"] != "trend" or finding["variables"][0]["id"] != variable or not finding["significant"]:
                continue
            cell = (finding["cell"]["row"], finding["cell"]["column"])
            found.add(cell)
            result = results.get(cell)
            outcome = outcome_of(finding["result"]["direction"], result, threshold)
            counts[outcome] += 1
            block = {"outcome": outcome, "product": dict(described)}
            if result is not None:
                block.update({
                    "direction": "increasing" if result.slope_per_decade > 0 else "decreasing",
                    "slope_per_decade": result.slope_per_decade,
                    "ci_low": result.ci_low, "ci_high": result.ci_high, "ci_level": result.ci_level,
                    "units": f"{product['units']} per decade",
                    "p_value": result.p_value,
                    "months_present": result.months_present, "months_expected": result.months_expected,
                })
            standing = finding["standing"]
            standing["retest"] = block
            standing["status"] = "retested" if outcome == "confirmed" else "exploratory"
            # The sentence saying it has not been re-tested is no longer true of any of these but the last.
            kept = [line for line in standing["limitations"] if line != EXPLORATORY]
            standing["limitations"] = kept + [sentence(outcome, product["name"])] + ([] if outcome == "not_possible" else [product["in_common"]])

        others = [result for cell, result in results.items() if cell not in found]
        summary.append({
            "variable": variable,
            "product": dict(described),
            "in_common": product["in_common"],
            "findings": sum(counts.values()),
            **counts,
            # Where the scan found nothing: how often the second product has a significant trend of its own.
            "other_cells": len(others),
            "other_cells_tested": sum(1 for result in others if result is not None),
            "other_cells_significant": sum(1 for result in others if result is not None and result.p_value < threshold),
        })

    run["retest"] = {"rule": RULE, "by_variable": summary}
    return document


def load_second(folder) -> dict[str, np.ndarray]:
    """The second products there are arrays for in ``folder``.

    A product's array is ``<variable>.npy`` unless it names another under ``array``: one file can
    serve two variables, as GISTEMP does for the air over land and the surface of the sea.
    """
    folder = Path(folder)
    found = {}
    for variable, product in SECOND_PRODUCTS.items():
        path = folder / f"{product.get('array', variable)}.npy"
        if path.exists():
            found[variable] = np.load(path)
    return found


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit("usage: python -m vigil.api.retest <snapshot.json> <folder of second products> <out.json>")
    stored = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    arrays = load_second(sys.argv[2])
    if not arrays:
        sys.exit(f"no second product found in {sys.argv[2]}")
    retest(stored, arrays)
    write(stored, sys.argv[3])
    for line in stored["run"]["retest"]["by_variable"]:
        print(f"{line['variable']:14} on {line['product']['mission']:22} {line['findings']:5d} findings: "
              f"{line['confirmed']} confirmed, {line['same_direction']} same direction, {line['opposite']} opposite, "
              f"{line['not_possible']} not possible. Elsewhere {line['other_cells_significant']} of "
              f"{line['other_cells_tested']} cells are significant in the second product.")
    print(f"Wrote {sys.argv[3]}. Now run contract/tools/check.py on it.")
