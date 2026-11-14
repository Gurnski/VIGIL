"""Checks a results document against the contract.

    python contract/tools/check.py                       the fixture, with the extra fixture checks
    python contract/tools/check.py path/to/results.json  any results document, such as a real snapshot

It reads the file strictly (NaN and Infinity are errors), then checks that the document agrees
with itself: links resolve, intervals contain their slopes, the grid matches the findings, and
each anomaly series can be rebuilt from its raw values.

If the `jsonschema` package is installed it also validates against contract/schema.json. The
rest needs only the standard library.
"""

import json
import sys
from pathlib import Path

CONTRACT = Path(__file__).resolve().parent.parent
REQUIRED_BLOCK = {"trend": "result", "change_point": "change_point", "cascade": "cascade",
                  "contradiction": "contradiction"}

errors = []
schema_notice_shown = False


def expect(condition, message):
    if not condition:
        errors.append(message)


def reject_constant(name):
    raise ValueError(f"{name} is not allowed. Write null for a missing value.")


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8"), parse_constant=reject_constant)


def validate_schema(document, definition=None):
    try:
        import jsonschema
    except ImportError:
        global schema_notice_shown
        if not schema_notice_shown:
            print("  (jsonschema is not installed, so the schema check was skipped)")
            schema_notice_shown = True
        return
    schema = load(CONTRACT / "schema.json")
    if definition:
        schema = {"$ref": f"#/$defs/{definition}", "$defs": schema["$defs"]}
    validator = jsonschema.Draft202012Validator(schema)
    for error in sorted(validator.iter_errors(document), key=lambda e: list(e.absolute_path))[:20]:
        errors.append(f"schema: {'/'.join(str(p) for p in error.absolute_path)}: {error.message[:160]}")


def check_cell(cell, grid, where):
    if grid is None:
        return
    size = grid["cell_degrees"]
    expect(cell["id"] == f"r{cell['row']:02d}c{cell['column']:03d}", f"{where}: cell id does not match its row and column")
    expect(0 <= cell["row"] < grid["rows"] and 0 <= cell["column"] < grid["columns"], f"{where}: cell is off the grid")
    expect(abs(cell["lat_min"] - (grid["lat_min"] + cell["row"] * size)) < 1e-9
           and abs(cell["lon_min"] - (grid["lon_min"] + cell["column"] * size)) < 1e-9
           and abs(cell["lat_max"] - cell["lat_min"] - size) < 1e-9
           and abs(cell["lon_max"] - cell["lon_min"] - size) < 1e-9, f"{where}: cell bounds do not match the grid")


def check_results(document):
    run = document.get("run")
    settings = run["settings"] if run else None
    grid = settings["grid"] if run else None
    threshold = settings["threshold"] if run else 0.05
    findings = document.get("findings", [])
    by_id = {f["id"]: f for f in findings}
    expect(len(by_id) == len(findings), "finding ids are not unique")
    series = {(s["cell_id"], s["variable"]): s for s in document.get("series", [])}
    grids = {g["variable"]: g for g in document.get("grids", [])}

    expect([f["rank"] for f in findings] == sorted(f["rank"] for f in findings), "findings are not in rank order")
    scores = [f["score"]["total"] for f in findings]
    expect(scores == sorted(scores, reverse=True), "rank does not follow the score")

    for f in findings:
        where = f["id"]
        if f["kind"] not in REQUIRED_BLOCK:
            errors.append(f"{where}: unknown kind {f['kind']!r}")
            continue
        expect(REQUIRED_BLOCK[f["kind"]] in f, f"{where}: a {f['kind']} needs a {REQUIRED_BLOCK[f['kind']]} block")
        check_cell(f["cell"], grid, where)
        expect(f["period"]["start"] <= f["period"]["end"], f"{where}: the period ends before it starts")
        pre = f["preprocessing"]
        expect(abs(pre["coverage"] - pre["months_present"] / pre["months_expected"]) < 1e-3, f"{where}: coverage is not months_present / months_expected")

        if settings:
            weights = settings["score_weights"]
            total = sum(weights[name] * f["score"][name] for name in weights)
            expect(abs(total - f["score"]["total"]) < 1e-3, f"{where}: score total is not the weighted sum of its parts")

        if "result" in f and f["kind"] == "trend":
            r = f["result"]
            expect(r["ci_low"] <= r["slope_per_decade"] <= r["ci_high"], f"{where}: the interval does not contain the slope")
            expect(r["p_adjusted"] >= r["p_value"] - 1e-12, f"{where}: adjusted p is below raw p")
            expect(f["significant"] == (r["p_adjusted"] < threshold), f"{where}: significant does not match the threshold")
            wanted = "none" if not f["significant"] else "increasing" if r["slope_per_decade"] > 0 else "decreasing"
            expect(r["direction"] == wanted, f"{where}: direction should be {wanted}")
            layer = grids.get(f["variables"][0]["id"])
            if layer:
                row, column = f["cell"]["row"], f["cell"]["column"]
                slope, p = layer["slope_per_decade"][row][column], layer["p_adjusted"][row][column]
                expect(slope is not None and abs(slope - r["slope_per_decade"]) <= 1e-3 * abs(slope) + 1e-9, f"{where}: the grid holds a different slope")
                expect(p is not None and abs(p - r["p_adjusted"]) <= 1e-2 * p + 1e-12, f"{where}: the grid holds a different adjusted p")

        if "change_point" in f:
            c = f["change_point"]
            expect(c["date_low"] <= c["date"] <= c["date_high"], f"{where}: the date range does not contain the date")
            expect(f["period"]["start"] <= c["date"] <= f["period"]["end"], f"{where}: the change point is outside the period")
            expect(abs(c["shift"] - (c["level_after"] - c["level_before"])) < 1e-6 + 1e-4 * abs(c["shift"]), f"{where}: shift is not level_after - level_before")

        if "cascade" in f:
            c = f["cascade"]
            ids = [v["id"] for v in f["variables"]]
            expect(ids == [c["leading_variable"], c["following_variable"]], f"{where}: variables must be the leading one, then the following one")
            expect(f["significant"] == (c["p_adjusted"] < threshold), f"{where}: significant does not match the threshold")
            if "correlations_by_lag" in c:
                expect(abs(c["correlations_by_lag"][c["lag_months"]] - c["correlation"]) < 1e-6, f"{where}: correlation is not the value at lag_months")

        if "contradiction" in f:
            c = f["contradiction"]
            check_cell(c["paired_cell"], grid, where + " paired cell")
            expect(c["slope_per_decade"] * c["paired_slope_per_decade"] < 0, f"{where}: the two slopes have the same sign")
            expect(c["paired_cell"]["id"] != f["cell"]["id"], f"{where}: a cell cannot contradict itself")

        for link in f["links"]:
            target = by_id.get(link["finding_id"])
            if document.get("grids") is None and target is None:
                continue  # a filtered list may leave out the far end of a link
            expect(target is not None, f"{where}: link to {link['finding_id']}, which is not in the file")
            if target:
                expect(link["kind"] == target["kind"], f"{where}: link kind differs from {target['id']}'s kind")
                expect(any(back["finding_id"] == f["id"] for back in target["links"]), f"{where}: {target['id']} does not link back")

    for (cell, variable), s in series.items():
        where = f"series {cell} {variable}"
        n = len(s["dates"])
        expect(len(s["raw"]) == n and len(s["anomaly"]) == n, f"{where}: dates, raw and anomaly differ in length")
        expect(s["dates"] == sorted(set(s["dates"])), f"{where}: dates are not in order or repeat")
        expect(all((a is None) == (b is None) for a, b in zip(s["raw"], s["anomaly"])), f"{where}: raw and anomaly are missing in different months")
        months = {}
        for date, value in zip(s["dates"], s["raw"]):
            if value is not None:
                months.setdefault(date[5:7], []).append(value)
        means = {m: sum(v) / len(v) for m, v in months.items()}
        scale = max((abs(v) for v in s["raw"] if v is not None), default=1.0)
        worst = max((abs(a - (r - means[d[5:7]])) for d, r, a in zip(s["dates"], s["raw"], s["anomaly"]) if r is not None), default=0)
        expect(worst < 1e-3 * scale, f"{where}: anomaly is not raw minus the monthly climatology (off by {worst:.3g})")

    for variable, g in grids.items():
        where = f"grid {variable}"
        for name in ("slope_per_decade", "p_adjusted", "skip_reason"):
            expect(len(g[name]) == g["rows"] and all(len(line) == g["columns"] for line in g[name]), f"{where}: {name} is not rows by columns")
        clash = sum((g["skip_reason"][r][c] is None) != (g["slope_per_decade"][r][c] is not None)
                    or (g["skip_reason"][r][c] is None) != (g["p_adjusted"][r][c] is not None)
                    for r in range(g["rows"]) for c in range(g["columns"]))
        expect(clash == 0, f"{where}: {clash} cells are neither scanned nor skipped cleanly")

    if run:
        counts = run["counts"]
        expect(counts["tests_run"] + counts["tests_skipped"] == counts["tests_total"], "run: tests_run + tests_skipped is not tests_total")
        expect(counts["tests_significant"] + counts["tests_not_significant"] == counts["tests_run"], "run: significant + not significant is not tests_run")
        expect(sum(counts["skipped_by_reason"].values()) == counts["tests_skipped"], "run: skipped_by_reason does not add up to tests_skipped")
        expect(counts["cells_total"] == grid["rows"] * grid["columns"], "run: cells_total is not rows x columns")
        if findings and "grids" in document:
            expect(counts["findings_total"] == len(findings), "run: findings_total differs from the list")
        for row in counts["by_variable"]:
            g = grids.get(row["variable"])
            if g:
                scanned = sum(v is not None for line in g["p_adjusted"] for v in line)
                passed = sum(v is not None and v < threshold for line in g["p_adjusted"] for v in line)
                expect(scanned == row["tests_run"] and passed == row["tests_significant"], f"run: counts for {row['variable']} differ from its grid")
    return by_id


def check_fixture_cases(document, by_id):
    """The fixture exists to exercise every shape the app must handle."""
    findings = document["findings"]
    expect(document.get("synthetic") is True, "fixture: synthetic must be true")
    expect({f["kind"] for f in findings} == set(REQUIRED_BLOCK), "fixture: not every kind is present")
    trends = [f for f in findings if f["kind"] == "trend"]
    expect({f["variables"][0]["id"] for f in trends} == set(document["run"]["settings"]["variables"]), "fixture: not every variable has a trend")
    expect({f["result"]["direction"] for f in trends} == {"increasing", "decreasing", "none"}, "fixture: not every direction is present")
    threshold = document["run"]["settings"]["threshold"]
    expect(any(f["result"]["p_value"] < threshold <= f["result"]["p_adjusted"] for f in trends), "fixture: no trend that passes before correction and fails after")
    expect({f["standing"]["status"] for f in findings} == {"exploratory", "retested"}, "fixture: not both standings")
    expect(any(v is None for s in document["series"] for v in s["raw"]), "fixture: no series with a missing month")
    series = {(s["cell_id"], s["variable"]) for s in document["series"]}
    for f in findings:
        cells = [f["cell"]["id"]] + ([f["contradiction"]["paired_cell"]["id"]] if "contradiction" in f else [])
        for cell in cells:
            for variable in f["variables"]:
                expect((cell, variable["id"]) in series, f"fixture: no series for {f['id']} ({cell} {variable['id']})")
    reasons = {v for g in document["grids"] for line in g["skip_reason"] for v in line}
    expect(reasons == {None, "ocean", "ice", "coverage"}, "fixture: not every skip reason is present")

    ask = load(CONTRACT / "fixture" / "ask.json")
    statuses = set()
    for example in ask["examples"]:
        validate_schema(example["request"], "ask_request")
        validate_schema(example["response"], "ask_response")
        statuses.add(example["response"]["status"])
        if example["response"]["status"] == "answered":
            expect(len(example["response"]["citations"]) > 0, "ask: an answered response cites nothing")
        for citation in example["response"]["citations"]:
            expect(citation["finding_id"] in by_id, f"ask: cites {citation['finding_id']}, which is not in the snapshot")
    expect(statuses == {"answered", "inconclusive", "out_of_scope"}, "fixture: ask.json does not show every status")


def main():
    is_fixture = len(sys.argv) < 2
    path = CONTRACT / "fixture" / "snapshot.json" if is_fixture else Path(sys.argv[1])
    print(f"Checking {path}")
    document = load(path)
    validate_schema(document)
    by_id = check_results(document)
    if is_fixture:
        check_fixture_cases(document, by_id)
    for message in errors:
        print("  FAIL", message)
    print(f"{len(errors)} problem{'s' if len(errors) != 1 else ''}" if errors else "OK")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
