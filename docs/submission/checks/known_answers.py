"""The brief's known-answer check: after the scan, look at places with well-documented change.

The boxes below were written down before any of them was looked at, and every result is
printed, whatever it is. Each is a zone analysis by the service's own code (vigil.api.zone),
so the trend test is the scan's. Zone p-values are not corrected for many tests.

usage: python known_answers.py <snapshot.json> <scan-data.npz> [out.json]
"""
import json
import sys

from vigil.api.app import Results
from vigil.api.document import ScanData
from vigil.api.zone import analyse_zone

# name: (lat_min, lat_max, lon_min, lon_max, what is documented there)
PLACES = {
    "California": (32.5, 42.5, -125.0, -115.0, "multi-year drought and warming"),
    "Himalaya and Tibetan Plateau": (27.5, 35.0, 77.5, 95.0, "warming faster than the global mean"),
    "Sahel": (12.5, 17.5, -15.0, 35.0, "re-greening since the 1980s droughts"),
    "Southern Amazon": (-15.0, -5.0, -65.0, -50.0, "a longer dry season and warming along the arc of deforestation"),
    "Loess Plateau, China": (35.0, 40.0, 105.0, 112.5, "greening after large-scale replanting"),
    "Murray-Darling basin": (-37.5, -27.5, 140.0, 150.0, "the Millennium drought and its end"),
}


def box(lat_min, lat_max, lon_min, lon_max):
    return [[lat_max, lon_min], [lat_max, lon_max], [lat_min, lon_max], [lat_min, lon_min]]


document = json.load(open(sys.argv[1], encoding="utf-8"))
data = ScanData.load(sys.argv[2])
results = Results(document, data)
out = []
for name, (lat_min, lat_max, lon_min, lon_max, documented) in PLACES.items():
    answer = analyse_zone(data, results.grids, results.run, box(lat_min, lat_max, lon_min, lon_max))
    print(f"\n{name} ({answer['zone']['cell_count']} cells). Documented: {documented}")
    row = {"place": name, "documented": documented, "cells": answer["zone"]["cell_count"], "box": [lat_min, lat_max, lon_min, lon_max], "variables": []}
    for summary in answer["summaries"]:
        result = summary["result"]
        if not summary["tested"]:
            line = f"not tested ({summary['skip_reason']})"
        elif result is None:
            line = "no trend to report"
        else:
            line = (f"{result['slope_per_decade']:+.3g} {result['units']} [{result['ci_low']:+.3g}, {result['ci_high']:+.3g}], "
                    f"p {result['p_value']:.2g}, {'significant' if summary['significant'] else 'not significant'}")
        print(f"   {summary['variable']:24} {line}")
        row["variables"].append({"variable": summary["variable"], "tested": summary["tested"], "significant": summary["significant"],
                                 "result": result, "cells_used": summary["cells_used"]})
    out.append(row)
if len(sys.argv) > 3:
    json.dump(out, open(sys.argv[3], "w", encoding="utf-8"), indent=1)
