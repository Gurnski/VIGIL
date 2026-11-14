"""Checks that an answer really rests on the findings it was given.

The model is told to cite findings and quote numbers exactly. This module checks
that it did, in plain code with no model involved:

1. Every cited finding id is one of the findings that were sent.
2. An ``answered`` reply cites at least one finding.
3. Every number in the answer appears in a cited finding (or in the run summary).

A number matches when it is the source value written to fewer digits, the way
the contract's own examples do: 0.72 matches 0.7213, and 5.4e-06 matches
5.4121e-06. A number that is not in the sources at all does not match, so the
model cannot slip in a figure it worked out itself. Signs are ignored, so
"cooling of 0.22" matches a slope of -0.22. A percentage matches the source
value as a fraction: 95% matches 0.95.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

# A number: digits, optional decimals, optional exponent. It must not sit inside
# a word or an identifier, so the 2 in M2TMNXSLV or GPM_3IMERGM is not a number.
_NUMBER = re.compile(r"(?<![A-Za-z0-9_.])(\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)(%?)")
_CELL_ID = re.compile(r"\br\d+c\d+\b")


@dataclass
class GroundingResult:
    ok: bool
    problems: list[str] = field(default_factory=list)


def check_answer(response: dict, findings: list[dict], run: dict | None = None) -> GroundingResult:
    """Check a model reply (``status``, ``answer``, ``citations``) against the findings sent."""
    problems: list[str] = []
    by_id = {f["id"]: f for f in findings}
    cited_ids = [c["finding_id"] for c in response.get("citations", [])]

    for finding_id in cited_ids:
        if finding_id not in by_id:
            problems.append(f"cites a finding that was not provided: {finding_id}")

    if response.get("status") == "answered" and not cited_ids:
        problems.append("status is answered but no finding is cited")

    allowed = _numbers_in([by_id[i] for i in cited_ids if i in by_id])
    if run is not None:
        allowed += _numbers_in([run])

    text = _strip_ids(response.get("answer", ""), list(by_id))
    for match in _NUMBER.finditer(text):
        shown, percent = match.group(1), bool(match.group(2))
        if not _is_in(shown, percent, allowed):
            problems.append(f"number not found in the cited findings: {shown}{'%' if percent else ''}")

    return GroundingResult(ok=not problems, problems=problems)


def _strip_ids(text: str, finding_ids: list[str]) -> str:
    """Remove finding and cell ids, whose digits are names rather than numbers."""
    for finding_id in sorted(finding_ids, key=len, reverse=True):
        text = text.replace(finding_id, " ")
    return _CELL_ID.sub(" ", text)


def _numbers_in(node) -> list[float]:
    """Every number in a structure, including digits written inside strings such as dates."""
    found: list[float] = []
    if isinstance(node, bool):
        return found
    if isinstance(node, (int, float)):
        found.append(abs(float(node)))
    elif isinstance(node, str):
        found.extend(float(m.group(1)) for m in _NUMBER.finditer(_CELL_ID.sub(" ", node)))
    elif isinstance(node, dict):
        for value in node.values():
            found.extend(_numbers_in(value))
    elif isinstance(node, (list, tuple)):
        for value in node:
            found.extend(_numbers_in(value))
    return found


def _is_in(shown: str, percent: bool, sources: list[float]) -> bool:
    value = float(shown)
    if percent:
        return any(_matches(shown, value / 100.0, s) for s in sources) or any(
            _matches(shown, value, s) for s in sources
        )
    return any(_matches(shown, value, s) for s in sources)


def _matches(shown: str, value: float, source: float) -> bool:
    """True if ``value`` is ``source`` written to the number of digits ``shown`` has."""
    if "e" in shown.lower():
        mantissa, exponent = re.split(r"[eE]", shown)
        decimals = len(mantissa.split(".")[1]) if "." in mantissa else 0
        half_unit = 0.5 * 10 ** (int(exponent) - decimals)
    elif "." in shown:
        half_unit = 0.5 * 10 ** (-len(shown.split(".")[1]))
    else:
        # A whole number must match exactly: "2" is not a rounding of 2.4.
        return value == source
    return abs(value - source) <= half_unit * (1 + 1e-9)
