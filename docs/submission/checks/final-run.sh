#!/usr/bin/env bash
# The final run: gather the arrays, scan, re-test, check, and print what the checks say.
# Run it from python/ with the virtual environment made, once every array is in data/scan/.
# It writes under data/scan/work/ and replaces data/scan/scan-data.npz. It does not replace
# data/scan/snapshot.json: copy work/snapshot-final.json over it when the checks pass.
set -u
HERE="$(cd "$(dirname "$0")" && pwd)"
export PYTHONUNBUFFERED=1 PYTHONIOENCODING=utf-8 PYTHONPATH=.
PY=.venv/Scripts/python
W=../data/scan/work
mkdir -p "$W"

echo "== build";  $PY -m vigil.pipeline.build || exit 10
cp ../data/scan/scan-data.npz "$W/scan-data-final.npz"
echo "== scan";   $PY -m vigil.api.document "$W/scan-data-final.npz" "$W/scan-final.json" || exit 11
echo "== retest"; $PY -m vigil.api.retest "$W/scan-final.json" ../data/scan/retest "$W/snapshot-final.json" || exit 12
echo "== check";  $PY ../contract/tools/check.py "$W/snapshot-final.json" || exit 13
echo "== accept"; $PY "$HERE/accept.py" "$W/snapshot-final.json" "$W/scan-data-final.npz" || exit 14
echo "== sanity"; $PY "$HERE/sanity.py" "$W/snapshot-final.json"
echo "== track";  $PY "$HERE/track.py" "$W/snapshot-final.json" "$W/scan-data-final.npz" ../data/scan/retest
echo "== known answers"; $PY "$HERE/known_answers.py" "$W/snapshot-final.json" "$W/scan-data-final.npz" "$W/known-answers.json"
echo "== done"
echo "When the checks pass: copy $W/snapshot-final.json over ../data/scan/snapshot.json, then write the website's files with"
echo "  $PY -m vigil.api.export ../data/scan/snapshot.json ../data/scan/scan-data.npz ../data/site"
