"""Do the sea surface temperature change points of mid-2013 show in the second record too?

Many change points in one month usually means a sensor or a processing change. The scan's record
is Aqua MODIS. The second record is GISTEMP, which over the sea is ERSST: ships and buoys.

A step is measured the same way in both: the mean anomaly of the 36 months after a date minus the
mean of the 36 months before it. A sea that is warming steadily gives a small positive step at any
date, so the step at the stored date is set beside the step in the same cell four years earlier and
four years later. A step that is the sensor's would be large in the scan's record and no larger
than usual in the second. Nothing here is a test. It only describes.

usage: python sst_2013.py <snapshot.json> <scan-data.npz> <retest folder>
"""
import collections
import json
import sys
from pathlib import Path

import numpy as np

from vigil.api.document import ScanData

WINDOW = 36
AWAY = 48

document = json.load(open(sys.argv[1], encoding="utf-8"))
data = ScanData.load(sys.argv[2])
first = data.data["sea_surface_temperature"]
second = np.load(Path(sys.argv[3]) / "temperature.npy")
dates = data.dates


def anomaly(series):
    out = np.full(series.shape, np.nan)
    for month in range(12):
        out[month::12] = series[month::12] - np.nanmean(series[month::12])
    return out


def step(series, at):
    if at - WINDOW < 0 or at + WINDOW > series.size:
        return np.nan
    before, after = series[at - WINDOW:at], series[at:at + WINDOW]
    if np.isfinite(before).sum() < WINDOW // 2 or np.isfinite(after).sum() < WINDOW // 2:
        return np.nan
    return float(np.nanmean(after) - np.nanmean(before))


points = []
for finding in document["findings"]:
    if finding["kind"] != "change_point" or finding["variables"][0]["id"] != "sea_surface_temperature":
        continue
    cell, change = finding["cell"], finding["change_point"]
    at = dates.index(change["date"])
    a, b = anomaly(first[cell["row"], cell["column"]]), anomaly(second[cell["row"], cell["column"]])
    points.append({
        "date": change["date"][:7], "low": change["date_low"][:7], "high": change["date_high"][:7],
        "lat": (cell["lat_min"] + cell["lat_max"]) / 2, "lon": (cell["lon_min"] + cell["lon_max"]) / 2,
        "scan": step(a, at), "second": step(b, at),
        "scan_away": [step(a, at - AWAY), step(a, at + AWAY)],
        "second_away": [step(b, at - AWAY), step(b, at + AWAY)],
    })

print(len(points), "change points in sea surface temperature")
print("by year:", dict(sorted(collections.Counter(p["date"][:4] for p in points).items())))
cluster = [p for p in points if "2013-03" <= p["date"] <= "2013-09"]
print(len(cluster), "of them fall between March and September 2013")


def median(values):
    values = np.array(values, dtype=float)
    return float(np.nanmedian(values)) if np.isfinite(values).any() else float("nan")


print(f"\nThe step across {WINDOW} months either side, median over the {len(cluster)} cells of mid-2013:")
print(f"   at the stored date         scan {median([p['scan'] for p in cluster]):+.2f} K   second record {median([p['second'] for p in cluster]):+.2f} K")
print(f"   {AWAY} months earlier          scan {median([p['scan_away'][0] for p in cluster]):+.2f} K   second record {median([p['second_away'][0] for p in cluster]):+.2f} K")
print(f"   {AWAY} months later            scan {median([p['scan_away'][1] for p in cluster]):+.2f} K   second record {median([p['second_away'][1] for p in cluster]):+.2f} K")

both = [p for p in cluster if np.isfinite(p["scan"]) and np.isfinite(p["second"])]
larger = sum(1 for p in both if p["second"] > np.nanmax(np.abs(p["second_away"])))
print(f"   cells where the second record's step at the stored date is larger than at either other date: {larger} of {len(both)}")
ratio = np.array([p["second"] / p["scan"] for p in both])
print(f"   the second record's step as a share of the scan's: median {np.median(ratio):.2f}, lowest {ratio.min():.2f}, highest {ratio.max():.2f}")
print("   The second record is smoothed over 1,200 km, so neighbouring cells are not separate pieces of evidence.")

print("\nWhere they are, by patch (the stored date's range is the model's own, from date_low to date_high):")
patches = {
    "Gulf of Alaska and Bering Sea": lambda p: p["lat"] > 45,
    "Western North Atlantic": lambda p: 20 < p["lat"] <= 45 and p["lon"] > -100,
    "Off Baja California and Hawaii": lambda p: p["lat"] <= 20 or (p["lat"] <= 45 and p["lon"] <= -100),
}
for name, belongs in patches.items():
    group = [p for p in cluster if belongs(p)]
    if not group:
        continue
    print(f"   {name}: {len(group)} cells, dates {min(p['date'] for p in group)} to {max(p['date'] for p in group)}, "
          f"ranges from {min(p['low'] for p in group)} to {max(p['high'] for p in group)}, "
          f"step scan {median([p['scan'] for p in group]):+.2f} K, second record {median([p['second'] for p in group]):+.2f} K")
