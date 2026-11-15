"""A variable year by year: each scanned cell's mean anomaly in each calendar year.

This is for looking, not for testing. The trend layer says how fast each cell is changing.
This says what each year was like against the cell's own normal, so a person can watch a
change arrive: a drying that sets in after 2015, a sea that steps warmer in 2013.

For each cell the stored scan tested, the monthly anomalies are the ones the scan itself used
(``vigil.analysis.trend.deseasonalise``: each month minus that calendar month's mean over the
period). A year's value is the mean of its months' anomalies. A year with fewer than
``MIN_MONTHS`` months of data has no value, so a record that starts in August does not get a
year made of five months. Cells the scan skipped have no values at all, so the picture shows
the same cells the trend layer does.

No test is run on these numbers and nothing here feeds a finding. The format is in
``contract/README.md`` under "A variable year by year".
"""

from __future__ import annotations

import numpy as np

from vigil.analysis.trend import deseasonalise
from vigil.api.document import CONTRACT_VERSION, VARIABLES, ScanData

#: A year needs at least this many months of data to be given a value.
MIN_MONTHS = 9

#: Significant figures kept. The values colour a map: more would only make the answer larger.
FIGURES = 4


class YearsError(ValueError):
    """The year-by-year view cannot be made. The message is fit to show the user."""


def annual_anomalies(scan_data: ScanData, layer: dict, variable: str, run_id: str | None) -> dict:
    """``GET /years``. ``layer`` is the stored scan's globe layer for the variable, which says which cells it tested."""
    if variable not in scan_data.data:
        raise YearsError(f"There is no variable called {variable} in this scan.")
    data = scan_data.data[variable]
    rows, columns, months = data.shape
    reasons = np.array(layer["skip_reason"], dtype=object)
    if reasons.shape != (rows, columns):
        raise YearsError(f"The stored layer for {variable} does not cover the scan grid.")
    tested = reasons == None                               # noqa: E711  (elementwise, on an object array)

    year_of = np.array([int(date[:4]) for date in scan_data.dates])
    years = sorted(set(year_of.tolist()))
    values = np.full((len(years), rows, columns), np.nan)
    for row, column in zip(*np.nonzero(tested)):
        anomaly = deseasonalise(data[row, column], scan_data.start_month)
        for index, year in enumerate(years):
            in_year = anomaly[year_of == year]
            present = in_year[np.isfinite(in_year)]
            if present.size >= MIN_MONTHS:
                values[index, row, column] = present.mean()

    finite = np.abs(values[np.isfinite(values)])
    grid = scan_data.grid
    return {
        "contract_version": CONTRACT_VERSION,
        "synthetic": scan_data.synthetic,
        "run_id": run_id,
        "variable": variable,
        "units": VARIABLES[variable]["units"],
        "years": years,
        "min_months": MIN_MONTHS,
        "rows": rows,
        "columns": columns,
        "lat_min": float(grid["lat_min"]),
        "lat_max": float(grid["lat_min"]) + rows * float(grid["cell_degrees"]),
        "lon_min": float(grid["lon_min"]),
        "lon_max": float(grid["lon_min"]) + columns * float(grid["cell_degrees"]),
        # For drawing only: where a colour scale centred on zero can end without one extreme year washing out the rest.
        "scale_max": _rounded(float(np.percentile(finite, 98))) if finite.size else None,
        "values": [[[_rounded(value) for value in line] for line in year] for year in values],
    }


def _rounded(value: float) -> float | None:
    if not np.isfinite(value):
        return None
    return float(f"{value:.{FIGURES}g}")
