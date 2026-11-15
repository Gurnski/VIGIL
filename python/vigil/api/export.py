"""Writes what a plain website needs to show a stored scan with no service behind it.

The public site is a bucket of files. The viewer there can read the results document, but a
stored scan carries series for the top of the ranking only, so every other finding has no
chart. This step writes the series out as files the viewer can fetch one at a time:

    <folder>/snapshot.json                       the results document, as given
    <folder>/snapshot.json.gz                    the same, compressed, for serving with Content-Encoding: gzip
    <folder>/series/<variable>/<cell_id>.json    one per cell and variable that a finding's evidence draws
    <folder>/years/<variable>.json               each variable year by year, as ``GET /years`` gives it

Each series is the one ``GET /series`` would give, made by the same code, so the website and
the service cannot disagree. Nothing is computed here that the service does not already compute.
The format is in ``contract/README.md`` under "Series as files beside the viewer".

    python -m vigil.api.export <snapshot.json> <scan-data.npz> <folder>
"""

from __future__ import annotations

import gzip
import json
import sys
from pathlib import Path

from vigil.api.app import Results
from vigil.api.document import ScanData, cells_and_variables


def series_documents(results: Results):
    """Yields ``(variable, cell_id, document)`` for every cell and variable the findings' evidence draws."""
    head = {**results.head, "run_id": results.run["id"] if results.run else None}
    done: set[tuple[str, str]] = set()
    for finding in results.findings:
        for cell, variable in cells_and_variables(finding):
            key = (variable, cell["id"])
            if key in done:
                continue
            done.add(key)
            series = results.series_for(cell, variable)
            if series is not None:
                yield variable, cell["id"], {**head, "series": [series]}


def export(results: Results, folder: Path) -> dict:
    """Writes the files and returns how many of each, for the caller to report."""
    folder.mkdir(parents=True, exist_ok=True)
    # allow_nan=False: a stray NaN must fail here, not in a browser that cannot read it.
    whole = json.dumps(results.document, allow_nan=False, separators=(",", ":")).encode("utf-8")
    (folder / "snapshot.json").write_bytes(whole)
    # mtime=0 so the same scan always gives the same bytes.
    (folder / "snapshot.json.gz").write_bytes(gzip.compress(whole, compresslevel=9, mtime=0))

    written = size = 0
    for variable, cell_id, document in series_documents(results):
        target = folder / "series" / variable / f"{cell_id}.json"
        target.parent.mkdir(parents=True, exist_ok=True)
        body = json.dumps(document, allow_nan=False, separators=(",", ":")).encode("utf-8")
        target.write_bytes(body)
        written += 1
        size += len(body)
    years = 0
    if results.scan_data is not None:
        from vigil.api.years import annual_anomalies

        run_id = results.run["id"] if results.run else None
        (folder / "years").mkdir(exist_ok=True)
        for variable, layer in results.grids.items():
            if variable in results.scan_data.data:
                field = annual_anomalies(results.scan_data, layer, variable, run_id)
                (folder / "years" / f"{variable}.json").write_bytes(
                    json.dumps(field, allow_nan=False, separators=(",", ":")).encode("utf-8"))
                years += 1
    return {"series": written, "series_bytes": size, "snapshot_bytes": len(whole),
            "snapshot_gzip_bytes": (folder / "snapshot.json.gz").stat().st_size, "years": years}


def main(arguments: list[str]) -> int:
    if len(arguments) != 3:
        print(__doc__.strip().splitlines()[-1].strip(), file=sys.stderr)
        return 2
    snapshot, data, folder = arguments
    results = Results(json.loads(Path(snapshot).read_text(encoding="utf-8")), ScanData.load(data))
    counts = export(results, Path(folder))
    megabyte = 1024 * 1024
    print(f"Wrote {folder}: snapshot.json ({counts['snapshot_bytes'] / megabyte:.1f} MB, "
          f"{counts['snapshot_gzip_bytes'] / megabyte:.1f} MB compressed), {counts['series']} series files "
          f"({counts['series_bytes'] / megabyte:.1f} MB) and {counts['years']} year-by-year files.")
    print("Put snapshot.json, series/ and years/ beside index.html. How to serve the snapshot compressed is in python/vigil/api/README.md.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
