"""The README's sanity list for a results document, and how it compares with an earlier one.

usage: python sanity.py <snapshot.json> [earlier.json]
"""
import collections
import json
import os
import sys

d = json.load(open(sys.argv[1], encoding="utf-8"))
run = d["run"]
c = run["counts"]
print(f"run {run['id']} code {run['code_version']} duration {run['duration_seconds']:.0f}s synthetic {d['synthetic']} size {os.path.getsize(sys.argv[1]) / 1e6:.1f} MB")
print(f"tests total {c['tests_total']} run {c['tests_run']} significant {c['tests_significant']} skipped {c['tests_skipped']} {c['skipped_by_reason']}")
print(f"findings {c['findings_total']} {c['findings_by_kind']} series {len(d['series'])}")
f = d["findings"]
dirs = collections.Counter((x["variables"][0]["id"], x["result"]["direction"]) for x in f if x["kind"] == "trend")
print("\nper variable: run, significant (share), rising / falling, skipped by reason")
for v in c["by_variable"]:
    layer = next(g for g in d["grids"] if g["variable"] == v["variable"])
    reasons = collections.Counter(x for row in layer["skip_reason"] for x in row if x)
    print(f"  {v['variable']:24} run {v['tests_run']:5d}  significant {v['tests_significant']:5d} ({100 * v['tests_significant'] / max(1, v['tests_run']):4.1f}%)  "
          f"up {dirs[(v['variable'], 'increasing')]:4d} down {dirs[(v['variable'], 'decreasing')]:4d}  skipped {dict(reasons)}")
cp = [x for x in f if x["kind"] == "change_point"]
months = collections.Counter((x["variables"][0]["id"], x["change_point"]["date"][:7]) for x in cp)
print(f"\nchange points {len(cp)}; most in one month: {months.most_common(6)}")
cas = collections.Counter(tuple(v["id"] for v in x["variables"]) for x in f if x["kind"] == "cascade")
print("cascades by pair:", dict(cas.most_common(12)))
con = collections.Counter(x["variables"][0]["id"] for x in f if x["kind"] == "contradiction")
print("contradictions by variable:", dict(con))
print("\ntop 25")
for x in f[:25]:
    cell = x["cell"]
    print(f"  #{x['rank']:<3d} {x['kind']:13} lat {(cell['lat_min'] + cell['lat_max']) / 2:6.1f} lon {(cell['lon_min'] + cell['lon_max']) / 2:7.1f}  {x['summary'][:100]}")
top = collections.Counter(tuple(v["id"] for v in x["variables"]) for x in f[:100])
print("top 100 by variables:", dict(top.most_common(10)))

if len(sys.argv) > 2:
    e = json.load(open(sys.argv[2], encoding="utf-8"))
    old = {x["id"]: x for x in e["findings"]}
    new = {x["id"]: x for x in f}
    shared = [i for i in old if i in new and old[i]["kind"] == "trend"]
    same = sum(1 for i in shared if old[i]["result"] == new[i]["result"])
    print(f"\nagainst {os.path.basename(sys.argv[2])}: {len(old)} findings there, {len(new)} here; trend findings in both {len(shared)}, with identical result blocks {same}")
    for variable in sorted({x['variables'][0]['id'] for x in e['findings'] if x['kind'] == 'trend'}):
        a = {i for i, x in old.items() if x["kind"] == "trend" and x["variables"][0]["id"] == variable}
        b = {i for i, x in new.items() if x["kind"] == "trend" and x["variables"][0]["id"] == variable}
        print(f"   {variable:16} trends there {len(a):5d} here {len(b):5d} in both {len(a & b):5d}")
