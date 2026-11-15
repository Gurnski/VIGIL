"""Builds the fixture files in contract/fixture/ from a fixed seed.

Run from the repository root:

    python contract/tools/make_fixture.py

Everything it writes is made up. The series are random numbers with a trend, a step or a
link to another series added on purpose, and the statistics are worked out from those
series so that the files agree with themselves. Nothing here is a real result.

It needs only the Python standard library.
"""

import json
import math
import random
from pathlib import Path

CONTRACT_VERSION = "0.1"
SEED = 20261114

ROWS, COLUMNS, CELL_DEGREES = 72, 144, 2.5
FIRST_YEAR, MONTHS = 2001, 300
THRESHOLD = 0.05
CI_LEVEL = 0.95
Z_95 = 1.959964
MAX_LAG = 6

HERE = Path(__file__).resolve().parent
OUT = HERE.parent / "fixture"

# The real weights, from WEIGHTS in python/vigil/analysis/score.py. Keep the two the same.
SCORE_WEIGHTS = {
    "effect_size": 0.30,
    "certainty": 0.20,
    "coverage": 0.05,
    "robustness": 0.20,
    "cross_system": 0.25,
}

# No group may take more than max_share of the first top_n places. See "rank" in the README.
RANK_CAP = {"top_n": 20, "max_share": 0.25}

VARIABLES = {
    "temperature": {
        "id": "temperature",
        "name": "Air temperature at 2 m",
        "units": "K",
        "mission": "MERRA-2",
        "product": "M2TMNXSLV",
        "field": "T2M",
        "source_url": "https://search.earthdata.nasa.gov/search?q=M2TMNXSLV",
        "native_resolution": "0.5 by 0.625 degrees, monthly",
    },
    "precipitation": {
        "id": "precipitation",
        "name": "Precipitation",
        "units": "mm per month",
        "mission": "GPM IMERG",
        "product": "GPM_3IMERGM",
        "field": "precipitation",
        "source_url": "https://search.earthdata.nasa.gov/search?q=GPM_3IMERGM",
        "native_resolution": "0.1 degrees, monthly",
    },
    "soil_moisture": {
        "id": "soil_moisture",
        "name": "Soil moisture, 0 to 10 cm",
        "units": "kg per m2",
        "mission": "GLDAS Noah",
        "product": "GLDAS_NOAH025_M",
        "field": "SoilMoi0_10cm_inst",
        "source_url": "https://search.earthdata.nasa.gov/search?q=GLDAS_NOAH025_M",
        "native_resolution": "0.25 degrees, monthly",
    },
    "vegetation": {
        "id": "vegetation",
        "name": "Vegetation (NDVI)",
        "units": "NDVI",
        "mission": "Terra MODIS",
        "product": "MOD13C2",
        "field": "CMG 0.05 Deg Monthly NDVI",
        "source_url": "https://search.earthdata.nasa.gov/search?q=MOD13C2",
        "native_resolution": "0.05 degrees, monthly",
    },
}

# Background field for the globe layer: (mean slope, spread of slope, standard error), per decade.
FIELD = {
    "temperature": (0.25, 0.20, 0.09),
    "precipitation": (0.0, 6.0, 3.5),
    "soil_moisture": (-0.1, 0.8, 0.5),
    "vegetation": (0.004, 0.012, 0.006),
}
DECIMALS = {"temperature": 2, "precipitation": 1, "soil_moisture": 2, "vegetation": 4}


# ---------------------------------------------------------------- grid and dates

def cell_at(lat, lon):
    """The cell that contains a point. Row 0 is the southernmost row, column 0 starts at 180 W."""
    row = int((lat + 90) // CELL_DEGREES)
    column = int((lon + 180) // CELL_DEGREES)
    return row, column


def cell_id(row, column):
    return f"r{row:02d}c{column:03d}"


def cell_object(row, column):
    return {
        "id": cell_id(row, column),
        "row": row,
        "column": column,
        "lat_min": -90 + row * CELL_DEGREES,
        "lat_max": -90 + (row + 1) * CELL_DEGREES,
        "lon_min": -180 + column * CELL_DEGREES,
        "lon_max": -180 + (column + 1) * CELL_DEGREES,
    }


def month_date(t):
    return f"{FIRST_YEAR + t // 12:04d}-{t % 12 + 1:02d}-01"


DATES = [month_date(t) for t in range(MONTHS)]
PERIOD = {"start": DATES[0], "end": DATES[-1], "step": "monthly"}


def load_mask():
    lines = (HERE / "land_mask.txt").read_text().split()
    assert len(lines) == ROWS and all(len(line) == COLUMNS for line in lines)
    return list(reversed(lines))  # the file is north first, the grid is south first


# ---------------------------------------------------------------- statistics

def sig(x, digits=6):
    if x is None:
        return None
    if x == 0:
        return 0.0
    return float(f"{x:.{digits}g}")


def p_from_z(z):
    return math.erfc(abs(z) / math.sqrt(2))


def climatology_anomaly(raw, decimals):
    """Subtracts each calendar month's own mean. Missing months stay missing."""
    sums, counts = [0.0] * 12, [0] * 12
    for t, value in enumerate(raw):
        if value is not None:
            sums[t % 12] += value
            counts[t % 12] += 1
    means = [sums[m] / counts[m] for m in range(12)]
    return [None if v is None else round(v - means[t % 12], decimals + 2) for t, v in enumerate(raw)]


def mann_kendall_sen(values):
    """Plain Mann-Kendall with Sen's slope per decade and its confidence interval. No tie or
    autocorrelation correction: the fixture's noise is independent, so none is needed here."""
    points = [(t / 120.0, v) for t, v in enumerate(values) if v is not None]
    n = len(points)
    s = 0
    slopes = []
    for i in range(n - 1):
        xi, yi = points[i]
        for j in range(i + 1, n):
            xj, yj = points[j]
            s += (yj > yi) - (yj < yi)
            slopes.append((yj - yi) / (xj - xi))
    slopes.sort()
    variance = n * (n - 1) * (2 * n + 5) / 18.0
    z = 0.0 if s == 0 else (s - 1) / math.sqrt(variance) if s > 0 else (s + 1) / math.sqrt(variance)
    count = len(slopes)
    middle = count // 2
    slope = slopes[middle] if count % 2 else (slopes[middle - 1] + slopes[middle]) / 2
    spread = Z_95 * math.sqrt(variance)
    low = slopes[max(0, int(round((count - spread) / 2)) - 1)]
    high = slopes[min(count - 1, int(round((count + spread) / 2)))]
    return {"slope": slope, "ci_low": low, "ci_high": high, "p": p_from_z(z), "n": n}


def lagged_correlations(leader, follower):
    """Pearson correlation of leader[t] with follower[t + lag], for each lag from 0 to MAX_LAG."""
    out = []
    for lag in range(MAX_LAG + 1):
        pairs = [(leader[t], follower[t + lag]) for t in range(MONTHS - lag)
                 if leader[t] is not None and follower[t + lag] is not None]
        n = len(pairs)
        mean_a = sum(a for a, _ in pairs) / n
        mean_b = sum(b for _, b in pairs) / n
        cov = sum((a - mean_a) * (b - mean_b) for a, b in pairs)
        var_a = sum((a - mean_a) ** 2 for a, _ in pairs)
        var_b = sum((b - mean_b) ** 2 for _, b in pairs)
        out.append((cov / math.sqrt(var_a * var_b), n))
    return out


def benjamini_hochberg(p_values):
    order = sorted(range(len(p_values)), key=lambda i: p_values[i])
    adjusted = [0.0] * len(p_values)
    running = 1.0
    m = len(p_values)
    for position in range(m - 1, -1, -1):
        i = order[position]
        running = min(running, p_values[i] * m / (position + 1))
        adjusted[i] = running
    return adjusted


# ---------------------------------------------------------------- series

class SeriesSpec:
    """One made-up monthly series. `drift` is the part left after the seasonal cycle is removed,
    kept so that another series can be made to follow it."""

    def __init__(self, rng, variable, mean, amplitude, peak_month, noise, trend=0.0,
                 step_at=None, step=0.0, follows=None, lag=0, coupling=0.0, missing=(), floor=None):
        drift = []
        for t in range(MONTHS):
            value = trend * (t - MONTHS / 2) / 120.0 + rng.gauss(0, noise)
            if step_at is not None and t >= step_at:
                value += step
            if follows is not None:
                value += coupling * follows.drift[max(0, t - lag)]
            drift.append(value)
        self.variable = variable
        self.drift = drift
        decimals = DECIMALS[variable]
        raw = []
        for t in range(MONTHS):
            value = mean + amplitude * math.cos(2 * math.pi * (t % 12 - peak_month) / 12) + drift[t]
            if floor is not None:
                value = max(floor, value)
            raw.append(None if t in missing else round(value, decimals))
        self.raw = raw
        self.anomaly = climatology_anomaly(raw, decimals)
        self.stats = mann_kendall_sen(self.anomaly)


def build_series(rng):
    """Every (place, variable) the findings need. Places are arbitrary land cells."""
    places = {
        "north": cell_at(61.25, 101.25),      # temperature, rising
        "wet": cell_at(21.25, 78.75),         # precipitation, rising
        "drying": cell_at(-8.75, -51.25),     # precipitation, soil moisture and vegetation fall in turn
        "green": cell_at(13.75, 3.75),        # vegetation, rising
        "brown": cell_at(13.75, 6.25),        # vegetation, falling, next door to "green"
        "shift": cell_at(-26.25, 133.75),     # soil moisture and vegetation step up
        "weak": cell_at(38.75, -98.75),       # temperature, passes before correction only
        "flat": cell_at(51.25, 23.75),        # precipitation, nothing there
    }
    s = {}
    s["north", "temperature"] = SeriesSpec(rng, "temperature", 268, 22, 6, 1.6, trend=0.55)
    s["wet", "precipitation"] = SeriesSpec(rng, "precipitation", 95, 110, 6, 20, trend=7, floor=0)

    rain = SeriesSpec(rng, "precipitation", 150, 90, 0, 28, trend=-14, floor=0)
    soil = SeriesSpec(rng, "soil_moisture", 24, 5, 1, 1.0, trend=-0.3, follows=rain, lag=1, coupling=0.05)
    plants = SeriesSpec(rng, "vegetation", 0.62, 0.12, 3, 0.012, trend=-0.004, follows=soil, lag=2, coupling=0.012)
    s["drying", "precipitation"], s["drying", "soil_moisture"], s["drying", "vegetation"] = rain, soil, plants

    s["green", "vegetation"] = SeriesSpec(rng, "vegetation", 0.28, 0.12, 8, 0.02, trend=0.012)
    s["brown", "vegetation"] = SeriesSpec(rng, "vegetation", 0.27, 0.11, 8, 0.02, trend=-0.010,
                                          missing=(40, 41, 187))

    s["shift", "soil_moisture"] = SeriesSpec(rng, "soil_moisture", 9, 2, 1, 0.9, step_at=110, step=1.6)
    s["shift", "vegetation"] = SeriesSpec(rng, "vegetation", 0.18, 0.03, 2, 0.012, step_at=113, step=0.025)

    # A trend strong enough to pass on its raw p-value and weak enough to fail after correction.
    for attempt in range(200):
        candidate = SeriesSpec(random.Random(SEED + 1000 + attempt), "temperature", 285, 13, 6, 1.3, trend=-0.21)
        if 0.035 < candidate.stats["p"] < 0.049:
            s["weak", "temperature"] = candidate
            break
    # A series with no trend in it and a p-value nowhere near the threshold.
    for attempt in range(200):
        candidate = SeriesSpec(random.Random(SEED + 2000 + attempt), "precipitation", 55, 18, 6, 16)
        if candidate.stats["p"] > 0.3:
            s["flat", "precipitation"] = candidate
            break
    assert ("weak", "temperature") in s and ("flat", "precipitation") in s
    return places, s


# ---------------------------------------------------------------- globe layers

def build_grids(rng, mask, places, series):
    """Per variable: a slope and a raw p-value for every scanned cell, and a reason for every
    skipped one. Cells that have a series take their numbers from it."""
    reserved = set(places.values())
    slope, p_raw, skip = {}, {}, {}
    for variable, (mean, spread, error) in FIELD.items():
        waves = [(rng.uniform(1, 4), rng.uniform(1, 5), rng.uniform(0, 6.3), rng.uniform(0, 6.3), rng.uniform(0.4, 1))
                 for _ in range(5)]
        total = sum(w[4] for w in waves)
        slope[variable] = [[None] * COLUMNS for _ in range(ROWS)]
        p_raw[variable] = [[None] * COLUMNS for _ in range(ROWS)]
        skip[variable] = [[None] * COLUMNS for _ in range(ROWS)]
        for row in range(ROWS):
            lat = -90 + (row + 0.5) * CELL_DEGREES
            for column in range(COLUMNS):
                lon = -180 + (column + 0.5) * CELL_DEGREES
                kind = mask[row][column]
                if kind == ".":
                    skip[variable][row][column] = "ocean"
                    continue
                if kind == "I":
                    skip[variable][row][column] = "ice"
                    continue
                if (row, column) not in reserved:
                    polar_night = variable == "vegetation" and lat > 67.5
                    if polar_night or rng.random() < 0.01:
                        skip[variable][row][column] = "coverage"
                        continue
                wave = sum(w[4] * math.sin(w[0] * math.radians(lat) + w[2]) * math.cos(w[1] * math.radians(lon) + w[3])
                           for w in waves) / total
                value = mean + spread * 1.6 * wave
                z = value / error + rng.gauss(0, 1)
                slope[variable][row][column] = value
                p_raw[variable][row][column] = p_from_z(z)
    for (place, variable), spec in series.items():
        row, column = places[place]
        assert mask[row][column] == "#", f"{place} is not on land"
        slope[variable][row][column] = spec.stats["slope"]
        p_raw[variable][row][column] = spec.stats["p"]

    index = [(v, r, c) for v in FIELD for r in range(ROWS) for c in range(COLUMNS) if p_raw[v][r][c] is not None]
    adjusted = benjamini_hochberg([p_raw[v][r][c] for v, r, c in index])
    p_adjusted = {v: [[None] * COLUMNS for _ in range(ROWS)] for v in FIELD}
    for (v, r, c), value in zip(index, adjusted):
        p_adjusted[v][r][c] = sig(value)  # rounded once, so every count and copy agrees
    return slope, p_adjusted, skip


# ---------------------------------------------------------------- findings

def short_name(variable):
    """The variable's name without its qualifier: "Soil moisture", not "Soil moisture, 0 to 10 cm"."""
    return VARIABLES[variable]["name"].split(",")[0].split(" (")[0]


def preprocessing(present):
    return {
        "regridding": f"Area-weighted mean onto the {CELL_DEGREES} degree grid",
        "aggregation": "Monthly mean",
        "anomaly": "Monthly climatology of the same cell over the full period, subtracted",
        "missing_data_rule": "A cell is scanned when at least 90% of months are present. Missing months are left out of the test.",
        "months_present": present,
        "months_expected": MONTHS,
        "coverage": sig(present / MONTHS, 4),
    }


#: The second product the one re-tested finding names. It is the real one for precipitation.
SECOND_PRODUCT = {
    "mission": "GPCP v3.3", "product": "GPCPMON", "field": "sat_gauge_precip", "units": "mm per month",
    "source_url": "https://search.earthdata.nasa.gov/search?q=GPCPMON", "native_resolution": "0.5 degrees, monthly",
}


def standing(retested=False, extra=()):
    limitations = ["Synthetic fixture value. Not a real result."]
    limitations.extend(extra)
    return {"status": "retested" if retested else "exploratory", "limitations": limitations}


def build_findings(rng, places, series, p_adjusted):
    findings = {}

    def base(kind, key, place, variables, present, summary):
        row, column = places[place]
        finding = {
            "id": f"{kind}.{key}",
            "kind": kind,
            "rank": 0,
            "summary": summary,
            "significant": True,
            "cell": cell_object(row, column),
            "period": dict(PERIOD),
            "variables": [dict(VARIABLES[v]) for v in variables],
            "preprocessing": preprocessing(present),
        }
        findings[finding["id"]] = finding
        return finding

    def direction_word(variable, rising):
        words = {
            "temperature": ("warming", "cooling"),
            "precipitation": ("getting wetter", "getting drier"),
            "soil_moisture": ("soil getting wetter", "soil drying"),
            "vegetation": ("greening", "browning"),
        }
        return words[variable][0 if rising else 1]

    # Trends: one for every series that has no placed step in it.
    trend_ids = {}
    for (place, variable), spec in series.items():
        if place == "shift":
            continue
        row, column = places[place]
        stats = spec.stats
        adjusted = p_adjusted[variable][row][column]
        passes = adjusted < THRESHOLD
        rising = stats["slope"] > 0
        units = f"{VARIABLES[variable]['units']} per decade"
        if passes:
            summary = (f"{VARIABLES[variable]['name']}: {direction_word(variable, rising)} at "
                       f"{sig(stats['slope'], 3):+g} {units}")
        else:
            summary = f"{VARIABLES[variable]['name']}: no significant trend"
        finding = base("trend", f"{variable}.{cell_id(row, column)}", place, [variable], stats["n"], summary)
        finding["significant"] = passes
        finding["method"] = {
            "test": "mann_kendall",
            "parameters": {"slope_estimator": "sen", "autocorrelation_correction": "none",
                           "multiple_testing": "benjamini_hochberg", "threshold": THRESHOLD},
        }
        finding["result"] = {
            "direction": ("increasing" if rising else "decreasing") if passes else "none",
            "slope_per_decade": sig(stats["slope"]),
            "units": units,
            "ci_low": sig(stats["ci_low"]),
            "ci_high": sig(stats["ci_high"]),
            "ci_level": CI_LEVEL,
            "p_value": sig(stats["p"]),
            "p_adjusted": sig(adjusted),
        }
        finding["standing"] = standing(retested=(place == "wet"))
        if place == "wet":
            # One finding that a second product confirmed, in the shape python/vigil/api/retest.py writes.
            # The second record's figures are made up like the rest: a little smaller and a little less sure.
            finding["standing"]["retest"] = {
                "outcome": "confirmed",
                "product": dict(SECOND_PRODUCT),
                "direction": "increasing" if rising else "decreasing",
                "slope_per_decade": sig(stats["slope"] * 0.86),
                "ci_low": sig(stats["ci_low"] * 0.78),
                "ci_high": sig(stats["ci_high"] * 0.94),
                "ci_level": CI_LEVEL,
                "units": units,
                "p_value": sig(min(0.04, stats["p"] * 40)),
                "months_present": MONTHS,
                "months_expected": MONTHS,
            }
            finding["standing"]["limitations"] += [
                "Re-tested on GPCP: the second record has a significant trend in the same direction.",
                "GPCP and IMERG are both satellite estimates adjusted to rain gauges, and they share gauge data and "
                "some satellite inputs. Agreement between them is weaker evidence than agreement with a record made another way.",
            ]
        trend_ids[place, variable] = finding["id"]

    # Change points: the step was placed by the generator, so the date is known exactly.
    change_ids = {}
    for variable, step_at in (("soil_moisture", 110), ("vegetation", 113)):
        spec = series["shift", variable]
        row, column = places["shift"]
        before = [v for v in spec.anomaly[:step_at] if v is not None]
        after = [v for v in spec.anomaly[step_at:] if v is not None]
        level_before, level_after = sum(before) / len(before), sum(after) / len(after)
        units = VARIABLES[variable]["units"]
        finding = base("change_point", f"{variable}.{cell_id(row, column)}", "shift", [variable], spec.stats["n"],
                       f"{VARIABLES[variable]['name']}: level shifts by {sig(level_after - level_before, 3):+g} "
                       f"{units} around {DATES[step_at][:7]}")
        finding["method"] = {"test": "placed_step",
                             "parameters": {"note": "Fixture only. The break was placed, not detected."}}
        finding["change_point"] = {
            "date": DATES[step_at],
            "date_low": DATES[step_at - 3],
            "date_high": DATES[step_at + 4],
            "level_before": sig(level_before),
            "level_after": sig(level_after),
            "shift": sig(level_after - level_before),
            "units": units,
        }
        finding["standing"] = standing()
        change_ids[variable] = finding["id"]

    # Cascades: the lag with the strongest correlation between two variables in one cell.
    cascade_specs = [("precipitation", "soil_moisture"), ("soil_moisture", "vegetation")]
    cascade_rows = []
    for leading, following in cascade_specs:
        a, b = series["drying", leading], series["drying", following]
        by_lag = lagged_correlations(a.anomaly, b.anomaly)
        lag = max(range(MAX_LAG + 1), key=lambda k: abs(by_lag[k][0]))
        r, n = by_lag[lag]
        p = p_from_z(r * math.sqrt((n - 2) / (1 - r * r)))
        cascade_rows.append((leading, following, by_lag, lag, r, n, p))
    cascade_adjusted = benjamini_hochberg([row[6] for row in cascade_rows])
    cascade_ids = []
    for (leading, following, by_lag, lag, r, n, p), adjusted in zip(cascade_rows, cascade_adjusted):
        row, column = places["drying"]
        finding = base("cascade", f"{leading}-{following}.{cell_id(row, column)}", "drying", [leading, following], n,
                       f"{short_name(following)} follows {short_name(leading).lower()} by "
                       f"{lag} month{'s' if lag != 1 else ''} (r = {sig(r, 2):+g}). An association, not a cause.")
        finding["significant"] = adjusted < THRESHOLD
        finding["method"] = {"test": "lagged_pearson_correlation",
                             "parameters": {"max_lag_months": MAX_LAG, "series": "anomaly", "detrended": False,
                                            "multiple_testing": "benjamini_hochberg", "threshold": THRESHOLD}}
        finding["cascade"] = {
            "leading_variable": leading,
            "following_variable": following,
            "lag_months": lag,
            "correlation": sig(r),
            "correlations_by_lag": [sig(value) for value, _ in by_lag],
            "p_value": sig(p),
            "p_adjusted": sig(adjusted),
        }
        finding["standing"] = standing(extra=[
            "Both series carry a long-term trend, which raises the correlation. Correlation does not establish cause."])
        cascade_ids.append((finding["id"], leading, following, lag, r))

    # Contradiction: neighbouring cells with significant trends of opposite sign.
    green, brown = findings[trend_ids["green", "vegetation"]], findings[trend_ids["brown", "vegetation"]]
    assert green["significant"] and brown["significant"]
    row, column = places["green"]
    other_row, other_column = places["brown"]
    mid_lat = math.radians(green["cell"]["lat_min"] + CELL_DEGREES / 2)
    contradiction = base("contradiction", f"vegetation.{cell_id(row, column)}-{cell_id(other_row, other_column)}",
                         "green", ["vegetation"], min(series["green", "vegetation"].stats["n"],
                                                      series["brown", "vegetation"].stats["n"]),
                         "Vegetation (NDVI): greening here, browning in the next cell east")
    contradiction["method"] = {"test": "opposite_significant_trends",
                               "parameters": {"neighbourhood": "cells sharing an edge", "threshold": THRESHOLD}}
    contradiction["contradiction"] = {
        "variable": "vegetation",
        "paired_cell": cell_object(other_row, other_column),
        "slope_per_decade": green["result"]["slope_per_decade"],
        "paired_slope_per_decade": brown["result"]["slope_per_decade"],
        "units": green["result"]["units"],
        "distance_km": round(6371.0 * math.radians(CELL_DEGREES) * math.cos(mid_lat)),
        "confounders": ["Different land cover or land use in the two cells",
                        "Irrigation or grazing in one cell and not the other"],
    }
    contradiction["standing"] = standing()

    # Links, written on both ends.
    def link(a, b, **extra_fields):
        for source, target in ((a, b), (b, a)):
            entry = {"finding_id": target, "kind": findings[target]["kind"]}
            entry.update(extra_fields)
            findings[source].setdefault("links", []).append(entry)

    for finding_id, leading, following, lag, r in cascade_ids:
        link(finding_id, trend_ids["drying", leading], lag_months=lag, correlation=sig(r))
        link(finding_id, trend_ids["drying", following], lag_months=lag, correlation=sig(r))
    link(cascade_ids[0][0], cascade_ids[1][0])
    link(contradiction["id"], green["id"], cell_id=brown["cell"]["id"])
    link(contradiction["id"], brown["id"], cell_id=green["cell"]["id"])
    link(change_ids["soil_moisture"], change_ids["vegetation"], lag_months=3)
    for finding in findings.values():
        finding.setdefault("links", [])

    # Scores. The weights are the real ones. The formulas for the parts are stand-ins:
    # the real ones are in python/vigil/analysis/score.py.
    for finding in findings.values():
        kind = finding["kind"]
        if kind == "trend":
            result = finding["result"]
            scale = 4 * FIELD[finding["variables"][0]["id"]][2]
            effect = min(1.0, abs(result["slope_per_decade"]) / scale)
            width = result["ci_high"] - result["ci_low"]
            certainty = max(0.0, 1 - width / (2 * abs(result["slope_per_decade"]) + 1e-12)) if finding["significant"] else 0.05
        elif kind == "cascade":
            effect = abs(finding["cascade"]["correlation"])
            certainty = 1 - min(1.0, finding["cascade"]["p_adjusted"] * 10)
        elif kind == "change_point":
            point = finding["change_point"]
            effect = min(1.0, abs(point["shift"]) / (4 * FIELD[finding["variables"][0]["id"]][2] * 2.5))
            certainty = 1 - 7 / 24
        else:
            block = finding["contradiction"]
            effect = min(1.0, abs(block["slope_per_decade"] - block["paired_slope_per_decade"]) / (8 * FIELD["vegetation"][2]))
            certainty = 0.7
        parts = {
            "effect_size": round(effect, 3),
            "certainty": round(certainty, 3),
            "coverage": round(finding["preprocessing"]["coverage"], 3),
            "robustness": round(rng.uniform(0.5, 0.9), 3) if finding["significant"] else 0.1,
            "cross_system": round(min(1.0, len(finding["links"]) / 3), 3),
        }
        total = sum(SCORE_WEIGHTS[name] * value for name, value in parts.items())
        finding["score"] = {"total": round(total, 4), **parts}

    # The same order as rank_findings in score.py: significant first, then by total,
    # then by id, then the cap on the significant ones.
    ranked = sorted(findings.values(), key=lambda f: (not f["significant"], -f["score"]["total"], f["id"]))
    limit = max(1, math.ceil(RANK_CAP["max_share"] * RANK_CAP["top_n"]))
    used, head = {}, []
    for finding in ranked:
        if len(head) >= RANK_CAP["top_n"] or not finding["significant"]:
            break
        group = "cascade" if finding["kind"] == "cascade" else finding["variables"][0]["id"]
        if used.get(group, 0) < limit:
            head.append(finding)
            used[group] = used.get(group, 0) + 1
    chosen = {finding["id"] for finding in head}
    ranked = head + [finding for finding in ranked if finding["id"] not in chosen]
    for position, finding in enumerate(ranked, start=1):
        finding["rank"] = position
    order = ["id", "kind", "rank", "summary", "significant", "cell", "period", "variables", "preprocessing", "method",
             "result", "change_point", "cascade", "contradiction", "links", "score", "standing"]
    return [{key: finding[key] for key in order if key in finding} for finding in ranked]


# ---------------------------------------------------------------- documents

def build_snapshot():
    rng = random.Random(SEED)
    mask = load_mask()
    places, series = build_series(rng)
    slope, p_adjusted, skip = build_grids(rng, mask, places, series)
    findings = build_findings(rng, places, series, p_adjusted)

    weak = next(f for f in findings if f["cell"]["id"] == cell_id(*places["weak"]))
    assert weak["result"]["p_value"] < THRESHOLD <= weak["result"]["p_adjusted"], "the weak trend must fail after correction"
    for finding in findings:
        if finding["kind"] != "trend":
            assert finding["significant"], finding["id"]

    grids, by_variable = [], []
    totals = {"run": 0, "significant": 0, "skipped": 0}
    reasons = {"ocean": 0, "ice": 0, "coverage": 0}
    eligible = set()
    for variable in FIELD:
        run = significant = 0
        variable_reasons = {"ocean": 0, "ice": 0, "coverage": 0}
        for row in range(ROWS):
            for column in range(COLUMNS):
                reason = skip[variable][row][column]
                if reason is not None:
                    variable_reasons[reason] += 1
                    continue
                run += 1
                eligible.add((row, column))
                significant += p_adjusted[variable][row][column] < THRESHOLD
        skipped = sum(variable_reasons.values())
        by_variable.append({"variable": variable, "tests_run": run, "tests_significant": significant,
                            "tests_not_significant": run - significant, "tests_skipped": skipped})
        totals["run"] += run
        totals["significant"] += significant
        totals["skipped"] += skipped
        for reason, count in variable_reasons.items():
            reasons[reason] += count
        grids.append({
            "variable": variable,
            "units": f"{VARIABLES[variable]['units']} per decade",
            "rows": ROWS,
            "columns": COLUMNS,
            "lat_min": -90.0, "lat_max": 90.0, "lon_min": -180.0, "lon_max": 180.0,
            "slope_per_decade": [[sig(v, 4) for v in line] for line in slope[variable]],
            "p_adjusted": p_adjusted[variable],
            "skip_reason": skip[variable],
        })

    kinds = {}
    for finding in findings:
        kinds[finding["kind"]] = kinds.get(finding["kind"], 0) + 1

    run_record = {
        "id": "fixture-0001",
        "started": "2026-11-13T21:00:00Z",
        "finished": "2026-11-13T21:14:30Z",
        "duration_seconds": 870,
        "code_version": "fixture",
        "settings": {
            "grid": {"cell_degrees": CELL_DEGREES, "rows": ROWS, "columns": COLUMNS, "lat_min": -90.0, "lon_min": -180.0},
            "period": dict(PERIOD),
            "variables": list(FIELD),
            "min_coverage": 0.9,
            "threshold": THRESHOLD,
            "multiple_testing": "benjamini_hochberg",
            "ci_level": CI_LEVEL,
            "max_lag_months": MAX_LAG,
            "score_weights": SCORE_WEIGHTS,
            "rank_cap": RANK_CAP,
        },
        "counts": {
            "cells_total": ROWS * COLUMNS,
            "cells_scanned": len(eligible),
            "tests_total": ROWS * COLUMNS * len(FIELD),
            "tests_run": totals["run"],
            "tests_significant": totals["significant"],
            "tests_not_significant": totals["run"] - totals["significant"],
            "tests_skipped": totals["skipped"],
            "skipped_by_reason": reasons,
            "by_variable": by_variable,
            "findings_total": len(findings),
            "findings_by_kind": kinds,
        },
    }

    series_out = []
    for (place, variable), spec in series.items():
        row, column = places[place]
        series_out.append({
            "cell_id": cell_id(row, column),
            "variable": variable,
            "units": VARIABLES[variable]["units"],
            "dates": DATES,
            "raw": spec.raw,
            "anomaly": spec.anomaly,
        })

    return {
        "contract_version": CONTRACT_VERSION,
        "synthetic": True,
        "run": run_record,
        "variables": [VARIABLES[v] for v in FIELD],
        "findings": findings,
        "series": series_out,
        "grids": grids,
    }


def build_ask(snapshot):
    by_id = {f["id"]: f for f in snapshot["findings"]}

    def first(kind, variable=None, significant=True):
        return next(f for f in snapshot["findings"] if f["kind"] == kind and f["significant"] == significant
                    and (variable is None or f["variables"][0]["id"] == variable))

    warming = first("trend", "temperature")
    cascades = {(f["cascade"]["leading_variable"], f["cascade"]["following_variable"]): f
                for f in snapshot["findings"] if f["kind"] == "cascade"}
    rain_soil = cascades["precipitation", "soil_moisture"]
    soil_plants = cascades["soil_moisture", "vegetation"]
    weak = first("trend", "temperature", significant=False)
    result = warming["result"]
    run_id = snapshot["run"]["id"]
    assert all(i in by_id for i in (warming["id"], rain_soil["id"], soil_plants["id"], weak["id"]))

    warming_answer = (f"The strongest warming in the scan is in cell {warming['cell']['id']}, where air "
                      f"temperature at 2 m rose by {result['slope_per_decade']:.2f} K per decade between 2001 "
                      f"and 2025 (95% interval {result['ci_low']:.2f} to {result['ci_high']:.2f}, adjusted "
                      f"p = {result['p_adjusted']:.2g}). The source is MERRA-2, product M2TMNXSLV.")

    def lag_text(finding):
        lag = finding["cascade"]["lag_months"]
        return f"{lag} month{'s' if lag != 1 else ''}"

    return {
        "contract_version": CONTRACT_VERSION,
        "synthetic": True,
        "examples": [
            {
                "request": {"question": "Where is it warming fastest?", "history": []},
                "response": {
                    "contract_version": CONTRACT_VERSION,
                    "run_id": run_id,
                    "status": "answered",
                    "answer": warming_answer,
                    "citations": [{"finding_id": warming["id"],
                                   "supports": "The rate of warming, its interval and its adjusted p-value"}],
                    "limitations": ["MERRA-2 is a reanalysis, not a direct satellite measurement.",
                                    "The finding is exploratory and has not been re-tested on held-out years."],
                    "model": "fixture",
                },
            },
            {
                "request": {"question": f"Is the drop in rainfall causing the vegetation loss in cell {rain_soil['cell']['id']}?",
                            "history": [{"role": "user", "text": "Where is it warming fastest?"},
                                        {"role": "assistant", "text": warming_answer}],
                            "finding_id": soil_plants["id"]},
                "response": {
                    "contract_version": CONTRACT_VERSION,
                    "run_id": run_id,
                    "status": "inconclusive",
                    "answer": (f"The results cannot settle that. In cell {rain_soil['cell']['id']}, soil moisture "
                               f"follows rainfall by {lag_text(rain_soil)} (correlation "
                               f"{rain_soil['cascade']['correlation']:.2f}), and vegetation follows soil moisture by "
                               f"{lag_text(soil_plants)} (correlation {soil_plants['cascade']['correlation']:.2f}). "
                               f"The timing fits rainfall, then soil, then vegetation, but these are associations in "
                               f"time. The scan has no test that separates cause from three series sharing a "
                               f"long-term trend."),
                    "citations": [{"finding_id": rain_soil["id"], "supports": "Soil moisture following rainfall"},
                                  {"finding_id": soil_plants["id"], "supports": "Vegetation following soil moisture"}],
                    "limitations": ["Lagged correlation shows timing, not cause.",
                                    "All three series carry a long-term trend, which raises the correlations."],
                    "model": "fixture",
                },
            },
            {
                "request": {"question": f"Is cell {weak['cell']['id']} cooling?", "history": []},
                "response": {
                    "contract_version": CONTRACT_VERSION,
                    "run_id": run_id,
                    "status": "inconclusive",
                    "answer": (f"There is no significant temperature trend in cell {weak['cell']['id']}. The raw "
                               f"p-value is {weak['result']['p_value']:.3f}, which is below 0.05, but after correcting "
                               f"for the number of tests in the scan it is {weak['result']['p_adjusted']:.3f}, which "
                               f"is not. The slope of {weak['result']['slope_per_decade']:.2f} K per decade could be "
                               f"chance."),
                    "citations": [{"finding_id": weak["id"], "supports": "The raw and adjusted p-values"}],
                    "limitations": [],
                    "model": "fixture",
                },
            },
            {
                "request": {"question": "What will the weather be like in Plymouth tomorrow?", "history": []},
                "response": {
                    "contract_version": CONTRACT_VERSION,
                    "run_id": run_id,
                    "status": "out_of_scope",
                    "answer": ("I can only answer from the findings of the VIGIL scan, which cover monthly changes "
                               "from 2001 to 2025. It holds no forecasts."),
                    "citations": [],
                    "limitations": [],
                    "model": "fixture",
                },
            },
        ],
    }


# ---------------------------------------------------------------- writing

def is_scalar(value):
    return value is None or isinstance(value, (str, int, float, bool))


def render(value, indent=0):
    """JSON with one line per row of numbers, so the grids stay readable and diffs stay small."""
    pad, inner = "  " * indent, "  " * (indent + 1)
    if isinstance(value, dict):
        if not value:
            return "{}"
        body = ",\n".join(f"{inner}{json.dumps(k)}: {render(v, indent + 1)}" for k, v in value.items())
        return "{\n" + body + "\n" + pad + "}"
    if isinstance(value, list):
        if all(is_scalar(v) for v in value):
            return json.dumps(value, allow_nan=False, ensure_ascii=False, separators=(",", ":") if len(value) > 12 else (", ", ": "))
        body = ",\n".join(inner + render(v, indent + 1) for v in value)
        return "[\n" + body + "\n" + pad + "]"
    return json.dumps(value, allow_nan=False, ensure_ascii=False)


def main():
    OUT.mkdir(exist_ok=True)
    snapshot = build_snapshot()
    ask = build_ask(snapshot)
    for name, document in (("snapshot.json", snapshot), ("ask.json", ask)):
        text = render(document) + "\n"
        json.loads(text)
        (OUT / name).write_text(text, encoding="utf-8", newline="\n")
        print(f"{name}: {len(text) / 1024:.0f} KB")
    print(f"{len(snapshot['findings'])} findings, {len(snapshot['series'])} series, {len(snapshot['grids'])} grids")


if __name__ == "__main__":
    main()
