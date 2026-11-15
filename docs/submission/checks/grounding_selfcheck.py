"""Would the grounding check accept a correct answer about each real finding?

No model is involved. For every trend finding, an answer is written the way the prompt asks:
the size as a positive number to three significant figures, the interval with its signs, the
adjusted p-value to two, the product and the period. Every figure in it is the finding's own,
so the check should pass. A failure here is a correct answer the Detective would throw away.

usage: python grounding_selfcheck.py <snapshot.json>
"""
import collections
import json
import sys

from vigil.detective.grounding import check_answer

document = json.load(open(sys.argv[1], encoding="utf-8"))
run = document["run"]
failures = collections.Counter()
examples = {}
checked = 0
for finding in document["findings"]:
    if finding["kind"] != "trend":
        continue
    result = finding["result"]
    variable = finding["variables"][0]
    verb = "rose" if result["slope_per_decade"] > 0 else "fell"
    counts = run["counts"]
    for style, answer in {
        "three figures": (f"{variable['name']} {verb} by {abs(result['slope_per_decade']):.3g} {result['units']} "
                          f"(95% interval {result['ci_low']:.3g} to {result['ci_high']:.3g}, adjusted p-value {result['p_adjusted']:.2g}), "
                          f"from {variable['mission']} {variable['product']}, {finding['period']['start'][:4]} to {finding['period']['end'][:4]}."),
        "with counts": (f"Rank {finding['rank']} of {counts['findings_total']:,} findings. The scan ran {counts['tests_run']:,} tests and "
                        f"{counts['tests_significant']:,} were significant. It {verb} by {abs(result['slope_per_decade']):.2g} {result['units']}."),
    }.items():
        checked += 1
        verdict = check_answer({"status": "answered", "answer": answer, "citations": [{"finding_id": finding["id"]}]}, [finding], run)
        if not verdict.ok:
            for problem in verdict.problems:
                kind = problem.split(":")[0] + " (" + style + ")"
                failures[kind] += 1
                examples.setdefault(kind, (answer, problem))

print(f"{checked} answers checked")
if not failures:
    print("every one passed")
for kind, count in failures.most_common():
    print(f"{count:6}  {kind}")
    print("        e.g.", examples[kind][1], "|", examples[kind][0][:200])
