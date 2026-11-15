"""Does each second product track the scan's own record, month by month?

For every cell the scan tested, the correlation between the two products' monthly anomalies.
A second product that is read wrongly (wrong field, wrong orientation, a constant) would
correlate near zero. The median, and the shares below 0.2 and above 0.5, are printed.

usage: python track.py <snapshot.json> <scan-data.npz> <retest folder>
"""
import json
import sys
from pathlib import Path

import numpy as np

from vigil.analysis.trend import deseasonalise
from vigil.api.document import ScanData
from vigil.api.retest import SECOND_PRODUCTS

document = json.load(open(sys.argv[1], encoding="utf-8"))
data = ScanData.load(sys.argv[2])
folder = Path(sys.argv[3])
layers = {layer["variable"]: layer for layer in document["grids"]}

for variable, product in SECOND_PRODUCTS.items():
    path = folder / f"{product.get('array', variable)}.npy"
    if not path.exists() or variable not in data.data:
        print(f"{variable:24} no array")
        continue
    second = np.load(path)
    first = data.data[variable]
    tested = np.array(layers[variable]["skip_reason"], dtype=object) == None      # noqa: E711
    found = {(f["cell"]["row"], f["cell"]["column"]): f for f in document["findings"]
             if f["kind"] == "trend" and f["significant"] and f["variables"][0]["id"] == variable}
    correlations, by_outcome = [], {}
    for row, column in zip(*np.nonzero(tested)):
        a = deseasonalise(first[row, column], data.start_month)
        b = deseasonalise(second[row, column], data.start_month)
        both = np.isfinite(a) & np.isfinite(b)
        if both.sum() < 60 or np.std(a[both]) == 0 or np.std(b[both]) == 0:
            continue
        r = float(np.corrcoef(a[both], b[both])[0, 1])
        correlations.append(r)
        finding = found.get((int(row), int(column)))
        if finding is not None and "retest" in finding["standing"]:
            by_outcome.setdefault(finding["standing"]["retest"]["outcome"], []).append(r)
    c = np.array(correlations)
    line = (f"{variable:24} vs {product['name']:14} cells {c.size:5d}  median r {np.median(c):+.2f}  "
            f"below 0.2: {100 * (c < 0.2).mean():4.1f}%  above 0.5: {100 * (c > 0.5).mean():4.1f}%")
    outcomes = "  ".join(f"{k} {np.median(v):+.2f} (n {len(v)})" for k, v in sorted(by_outcome.items()))
    print(line + ("  | median r by outcome: " + outcomes if outcomes else ""))
