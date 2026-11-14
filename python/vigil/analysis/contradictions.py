"""Contradictions: neighbouring cells whose trends point opposite ways.

A contradiction is two cells that sit next to each other, hold a significant
trend in the same variable, and have slopes of opposite sign. Greening in one
cell and browning in the next is the example in the contract. It is a question
for the reader ("what differs between these two places?"), never an answer.

    from vigil.analysis.contradictions import add_contradictions

    combined = add_contradictions(
        result.findings, result.evidence,          # from scan_trends
        grid=GRID, threshold=0.05, rank_cap=RANK_CAP,
    )
    combined.findings    # the trends and the contradictions, ranked together, links written
    combined.evidence    # the Evidence for all of them, to rank again when more kinds arrive

Nothing new is tested. Both trends already passed the false-discovery correction
in the scan, so a contradiction is significant exactly when both of its trends
are, and it has no p-value of its own. The contract's ``contradiction`` block has
none either.

Which pairs
-----------
Two cells are neighbours when they share an edge (north, south, east or west), as
in the contract's fixture. ``include_diagonals=True`` adds cells that touch at a
corner. Columns wrap round the dateline when the grid spans 360 degrees; rows do
not wrap over the poles. Each pair is one finding, whichever cell is the "own" one,
and the own cell is the one whose id sorts first.

Variables in ``exclude_variables`` (default ``NOT_CROSS_SYSTEM``, the earthquakes)
are skipped. Neighbouring seismic zones that rise and fall are normal aftershock
and detection effects, not something to flag as a contradiction.

The finding
-----------
* ``id`` is ``contradiction.<variable>.<cell>-<other cell>``, own cell first.
* ``cell`` is the own cell, ``contradiction.paired_cell`` the other, each with its
  slope. ``distance_km`` is the great-circle distance between the two cell centres.
* ``preprocessing`` takes the lower coverage of the two trends. The text fields are
  the pipeline's, as for a trend.
* ``method`` is ``opposite_significant_trends`` with the neighbourhood and threshold.
* ``links``: the contradiction links to both trends and each of the two trends links
  back, with ``cell_id`` naming the cell on the other side. Trend findings returned
  here always carry a ``links`` list. The pipeline must therefore only add
  ``links = []`` where there is none (``finding.setdefault("links", [])``), and must
  not overwrite it.
* ``confounders``: plain sentences on what else could explain the difference. They are
  possibilities that are true of any pair of cells, not claims about these two.

Scoring
-------
The effect is half the gap between the two slopes, in units of the noise. The noise is
the larger of the two cells' (a pair is no more certain than its noisier cell). The
certainty and the robustness are the lower of the two trends'. ``rank_findings`` does the
rest, with the same weights and cap as the trends.

What the pipeline still adds, as for a trend: ``variables`` metadata (the entry here
holds only ``id`` and ``units``), ``period``, ``summary``, ``standing``, and the text
fields of ``preprocessing``. It also needs a ``series`` for each cell of a pair, so the
evidence view can draw both.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

from vigil.analysis.scan import check_cell_offset
from vigil.analysis.score import NOT_CROSS_SYSTEM, Evidence, rank_findings, trend_certainty

METHOD = "opposite_significant_trends"
EDGE = "cells sharing an edge"
EDGE_OR_CORNER = "cells sharing an edge or a corner"
EARTH_RADIUS_KM = 6371.0088
KM_PER_DEGREE = 111.0

# Sentences true of any two cells. Not claims about a particular pair.
GENERAL_CONFOUNDERS = (
    "A change in the instrument or the processing that affected one cell and not the other would look like opposite trends",
)
CONFOUNDERS_BY_VARIABLE = {
    "temperature": ("A coast or a large lake in one cell but not the other, since land and water warm at different rates",),
    "precipitation": ("A shift in a storm track or rain band can move rain from one cell into the next, so one gains what the other loses",),
    "soil_moisture": ("Different soil type or land cover in the two cells", "Irrigation or drainage in one cell and not the other"),
    "vegetation": ("Different land cover or land use in the two cells", "Irrigation, grazing, clearing or fire in one cell and not the other"),
}


@dataclass
class ContradictionResult:
    """The trends and the contradictions together, ranked, with links written on both ends."""

    findings: list[dict]
    evidence: dict[str, Evidence]
    contradictions: int


def cell_centre_km_apart(a: dict, b: dict) -> float:
    """Great-circle distance in km between the centres of two ``cell`` objects."""
    lat1, lon1 = math.radians((a["lat_min"] + a["lat_max"]) / 2), math.radians((a["lon_min"] + a["lon_max"]) / 2)
    lat2, lon2 = math.radians((b["lat_min"] + b["lat_max"]) / 2), math.radians((b["lon_min"] + b["lon_max"]) / 2)
    h = math.sin((lat2 - lat1) / 2) ** 2 + math.cos(lat1) * math.cos(lat2) * math.sin((lon2 - lon1) / 2) ** 2
    return 2 * EARTH_RADIUS_KM * math.asin(math.sqrt(h))


def confounders(variable: str, cell_degrees: float) -> list[str]:
    size = int(round(cell_degrees * KM_PER_DEGREE, -1))
    return [
        f"Each cell is an average over an area roughly {size} km across, so a coastline, a mountain range or a change of "
        "land cover inside or between the cells can give them different trends",
        *CONFOUNDERS_BY_VARIABLE.get(variable, ()),
        *GENERAL_CONFOUNDERS,
    ]


def _forward_offsets(include_diagonals: bool) -> list[tuple[int, int]]:
    """Offsets to the neighbours that each pair is found from once: east, north, and optionally the two corners above."""
    offsets = [(0, 1), (1, 0)]
    if include_diagonals:
        offsets += [(1, 1), (1, -1)]
    return offsets


def _contradiction(own: dict, other: dict, grid: dict, threshold: float, neighbourhood: str):
    variable = own["variables"][0]["id"]
    a, b = own["result"], other["result"]
    pre_a, pre_b = own["preprocessing"], other["preprocessing"]
    weaker = pre_a if pre_a["coverage"] <= pre_b["coverage"] else pre_b
    return {
        "id": f"contradiction.{variable}.{own['cell']['id']}-{other['cell']['id']}",
        "kind": "contradiction",
        "significant": True,
        "cell": own["cell"],
        "variables": [{"id": variable, "units": own["variables"][0].get("units")}],
        "preprocessing": {
            "months_present": weaker["months_present"],
            "months_expected": weaker["months_expected"],
            "coverage": weaker["coverage"],
        },
        "method": {"test": METHOD, "parameters": {"neighbourhood": neighbourhood, "threshold": threshold}},
        "contradiction": {
            "variable": variable,
            "paired_cell": other["cell"],
            "slope_per_decade": a["slope_per_decade"],
            "paired_slope_per_decade": b["slope_per_decade"],
            "units": a["units"],
            "distance_km": round(cell_centre_km_apart(own["cell"], other["cell"]), 1),
            "confounders": confounders(variable, float(grid["cell_degrees"])),
        },
    }


def add_contradictions(
    findings: list[dict],
    evidence: dict[str, Evidence],
    *,
    grid: dict,
    threshold: float,
    rank_cap: dict | None = None,
    weights: dict | None = None,
    include_diagonals: bool = False,
    exclude_variables=NOT_CROSS_SYSTEM,
    cell_offset: tuple[int, int] = (0, 0),
) -> ContradictionResult:
    """Find the contradictions among the significant trend findings and rank everything together.

    ``findings`` and ``evidence`` are ``ScanResult.findings`` and ``ScanResult.evidence``
    (any other kinds already in the list pass through untouched). ``grid`` is
    ``run.settings.grid`` and must hold ``cell_degrees``, ``rows`` and ``columns``.
    ``threshold`` is the scan's, written into ``method.parameters``. ``rank_cap`` and
    ``weights`` are the ones used for the scan. Inputs are not changed.

    ``cell_offset`` is the one the scan was given for a region (its cells' ids are the full grid's).
    Neighbours are found from those ids, so it only matters for the wrap round the dateline,
    which needs the region to span all 360 degrees.
    """
    cell_offset = check_cell_offset(cell_offset)
    for key in ("cell_degrees", "rows", "columns"):
        if key not in grid:
            raise ValueError(f"grid is missing {key!r}")
    wraps = int(grid["columns"]) * float(grid["cell_degrees"]) >= 360 - 1e-9
    neighbourhood = EDGE_OR_CORNER if include_diagonals else EDGE

    # significant trends by variable and position
    where: dict[tuple[str, int, int], dict] = {}
    for f in findings:
        variable = f["variables"][0]["id"]
        if f["kind"] == "trend" and f["significant"] and variable not in set(exclude_variables):
            where[(variable, f["cell"]["row"], f["cell"]["column"])] = f

    new: list[tuple[dict, dict, dict]] = []        # (contradiction, own trend, other trend)
    new_evidence: dict[str, Evidence] = {}
    for (variable, row, column), a in sorted(where.items()):
        for d_row, d_column in _forward_offsets(include_diagonals):
            n_column = column + d_column
            if wraps:
                n_column = (n_column - cell_offset[1]) % int(grid["columns"]) + cell_offset[1]
            b = where.get((variable, row + d_row, n_column))
            if b is None or a["result"]["slope_per_decade"] * b["result"]["slope_per_decade"] >= 0:
                continue
            own, other = sorted((a, b), key=lambda f: f["cell"]["id"])
            finding = _contradiction(own, other, grid, threshold, neighbourhood)
            if finding["id"] in new_evidence:
                continue                           # a very narrow wrap can reach the same pair twice
            ea, eb = evidence[a["id"]], evidence[b["id"]]
            new.append((finding, own, other))
            new_evidence[finding["id"]] = Evidence(
                noise_sd=max(ea.noise_sd, eb.noise_sd),
                robustness=min(ea.robustness, eb.robustness),
                certainty=min(
                    trend_certainty(f["result"]["slope_per_decade"], f["result"]["ci_low"], f["result"]["ci_high"])
                    for f in (a, b)
                ),
            )

    # links, on both ends: the trends' own lists are extended in copies, never in place
    linked = {f["id"]: {**f, "links": list(f.get("links", []))} for f in findings}
    for finding, own, other in new:
        finding["links"] = []
        for trend, far_cell in ((own, other["cell"]["id"]), (other, own["cell"]["id"])):
            finding["links"].append({"finding_id": trend["id"], "kind": "trend", "cell_id": far_cell})
            linked[trend["id"]]["links"].append({"finding_id": finding["id"], "kind": "contradiction", "cell_id": far_cell})

    combined = list(linked.values()) + [finding for finding, _, _ in new]
    all_evidence = {**evidence, **new_evidence}
    cap = {"top_n": int(rank_cap["top_n"]), "max_share": float(rank_cap["max_share"])} if rank_cap is not None else {}
    ranked = rank_findings(combined, all_evidence, weights=weights, diversify=rank_cap is not None, **cap)
    return ContradictionResult(findings=ranked, evidence=all_evidence, contradictions=len(new))
