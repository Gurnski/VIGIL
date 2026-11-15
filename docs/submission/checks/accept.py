"""Acceptance on a stored scan and its gridded data: for one finding of every variable, a zone of
that one cell and a fresh analysis of that one cell must both give the stored slope, interval and p-value.

usage: python accept.py <snapshot.json> <scan-data.npz>
"""
import json
import sys

from vigil.api.app import Results
from vigil.api.document import ScanData, analyse_region
from vigil.api.zone import analyse_zone

document = json.load(open(sys.argv[1], encoding="utf-8"))
data = ScanData.load(sys.argv[2])
results = Results(document, data)          # raises if the pair does not match
keys = ["slope_per_decade", "ci_low", "ci_high", "p_value"]


def around(c):
    return [[c["lat_max"] - .2, c["lon_min"] + .2], [c["lat_max"] - .2, c["lon_max"] - .2], [c["lat_min"] + .2, c["lon_max"] - .2], [c["lat_min"] + .2, c["lon_min"] + .2]]


def same(a, b):
    return all(abs(a[k] - b[k]) <= 1e-12 * max(1.0, abs(b[k])) for k in keys)


ok = True
for variable in data.data:
    finding = next((f for f in results.findings if f["kind"] == "trend" and f["variables"][0]["id"] == variable), None)
    if finding is None:
        print(f"{variable:24} no trend finding to check")
        continue
    cell = finding["cell"]
    zone = analyse_zone(data, results.grids, results.run, around(cell))
    summary = next(s for s in zone["summaries"] if s["variable"] == variable)
    zone_ok = zone["zone"]["cells"] == [cell["id"]] and summary["result"] is not None and same(summary["result"], finding["result"])
    region = analyse_region(data, cell["lat_min"], cell["lat_max"], cell["lon_min"], cell["lon_max"])
    again = next((f for f in region["findings"] if f["id"] == finding["id"]), None)
    region_ok = again is not None and same(again["result"], finding["result"])
    ok = ok and zone_ok and region_ok
    print(f"{variable:24} {finding['id']:42} slope {finding['result']['slope_per_decade']:+.5g}  one-cell zone {'SAME' if zone_ok else 'DIFFERENT'}  one-cell rerun {'SAME' if region_ok else 'DIFFERENT'}")
print("ALL SAME" if ok else "SOMETHING DIFFERS")
