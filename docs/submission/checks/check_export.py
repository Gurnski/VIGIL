"""Checks an exported CSV and GeoJSON against the results document they were made from.

usage: python check_export.py <snapshot.json> <file.csv> <file.geojson>
"""
import csv
import json
import sys

COLUMNS = ["rank", "id", "kind", "significant", "summary", "variables", "cell_id", "lat_centre", "lon_centre", "lat_min", "lat_max",
           "lon_min", "lon_max", "period_start", "period_end", "slope_per_decade", "units", "ci_low", "ci_high", "ci_level", "p_value",
           "p_adjusted", "change_date", "shift", "lag_months", "correlation", "paired_cell_id", "standing", "retest_outcome",
           "retest_product", "score", "run_id", "synthetic"]

document = json.load(open(sys.argv[1], encoding="utf-8"))
findings = sorted(document["findings"], key=lambda f: f["rank"])
problems = []


def expect(condition, message):
    if not condition:
        problems.append(message)


raw = open(sys.argv[2], "rb").read()
expect(not raw.startswith(b"\xef\xbb\xbf"), "the CSV starts with a byte order mark")
rows = list(csv.reader(raw.decode("utf-8").splitlines()))
expect(rows[0] == COLUMNS, f"CSV header differs: {rows[0]}")
expect(len(rows) - 1 == len(findings), f"CSV has {len(rows) - 1} rows for {len(findings)} findings")
expect(all(len(row) == len(COLUMNS) for row in rows), "a CSV row has the wrong number of fields")
table = [dict(zip(COLUMNS, row)) for row in rows[1:]]

geo = json.load(open(sys.argv[3], encoding="utf-8"))
expect(geo["type"] == "FeatureCollection", "not a FeatureCollection")
expect(geo["vigil"]["run_id"] == document["run"]["id"] and geo["vigil"]["synthetic"] == bool(document.get("synthetic")), "the vigil member is wrong")
expect(len(geo["features"]) == len(findings), "feature count differs")


def close(a, b):
    return a == b or (a is not None and b is not None and abs(float(a) - float(b)) <= 1e-12 * max(1.0, abs(float(b))))


kinds = {}
for finding, row, feature in zip(findings, table, geo["features"]):
    kinds[finding["kind"]] = kinds.get(finding["kind"], 0) + 1
    cell = finding["cell"]
    props = feature["properties"]
    expect(row["id"] == finding["id"] == feature["id"] == props["id"], f"id differs at rank {finding['rank']}")
    expect(int(row["rank"]) == finding["rank"] == props["rank"], "rank differs")
    expect(row["summary"] == finding["summary"] == props["summary"], f"summary differs for {finding['id']}")
    expect(list(props) == COLUMNS, "GeoJSON properties are not the contract's columns in order")
    ring = feature["geometry"]["coordinates"][0]
    expect(feature["geometry"]["type"] == "Polygon" and len(ring) == 5 and ring[0] == ring[-1], "ring is not closed")
    expect(ring[0] == [cell["lon_min"], cell["lat_min"]] and ring[2] == [cell["lon_max"], cell["lat_max"]], "ring corners are wrong")
    area = sum((ring[i][0] * ring[i + 1][1] - ring[i + 1][0] * ring[i][1]) for i in range(4))
    expect(area > 0, "ring is not counter-clockwise")
    retest = finding["standing"].get("retest")
    expect(row["standing"] == finding["standing"]["status"], "standing differs")
    expect(row["retest_outcome"] == (retest["outcome"] if retest else ""), "retest outcome differs")
    if finding["kind"] == "trend":
        result = finding["result"]
        for column in ("slope_per_decade", "ci_low", "ci_high", "p_value", "p_adjusted"):
            expect(close(row[column], result[column]) and close(props[column], result[column]), f"{column} differs for {finding['id']}: {row[column]} vs {result[column]}")
        expect(row["units"] == result["units"], "units differ")
    else:
        expect(row["ci_low"] == "" and props["ci_low"] is None, "a non-trend has an interval")
    if finding["kind"] == "change_point":
        expect(row["change_date"] == finding["change_point"]["date"] and close(row["shift"], finding["change_point"]["shift"]), "change point differs")
    if finding["kind"] == "cascade":
        expect(int(row["lag_months"]) == finding["cascade"]["lag_months"] and close(row["correlation"], finding["cascade"]["correlation"]), "cascade differs")
    if finding["kind"] == "contradiction":
        expect(row["paired_cell_id"] == finding["contradiction"]["paired_cell"]["id"], "paired cell differs")
    expect(close(row["score"], finding["score"]["total"]), "score differs")

print(f"{len(findings)} findings checked: {kinds}")
print("\n".join(sorted(set(problems))[:20]) if problems else "CSV and GeoJSON agree with the results document")
sys.exit(1 if problems else 0)
