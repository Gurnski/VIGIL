"""False-discovery-rate control across many tests.

A scan runs thousands of trend tests. Left alone, about 5% of them would come
out "significant" by chance. Benjamini-Hochberg control keeps the share of false
discoveries among the significant results near ``alpha``.

Both the raw and the adjusted p-values are kept, because the contract reports
both. A test that was skipped has a NaN p-value: it takes no part in the
correction and comes back as NaN.
"""

from __future__ import annotations

import numpy as np
from statsmodels.stats.multitest import multipletests


def benjamini_hochberg(p_values, alpha: float = 0.05):
    """Adjust one family of p-values.

    Returns ``(p_adjusted, significant)``, arrays the same shape as the input.
    NaN inputs stay NaN in ``p_adjusted`` and are ``False`` in ``significant``.
    """
    p = np.asarray(p_values, dtype=float)
    adjusted = np.full(p.shape, np.nan)
    significant = np.zeros(p.shape, dtype=bool)

    finite = ~np.isnan(p)
    if finite.any():
        rejected, corrected, _, _ = multipletests(p[finite], alpha=alpha, method="fdr_bh")
        adjusted[finite] = corrected
        significant[finite] = rejected
    return adjusted, significant


def adjust_by_family(p_values, families, alpha: float = 0.05):
    """Adjust p-values separately within each family.

    ``families`` gives a label for every p-value, such as the finding kind and
    variable. Tests are only corrected against others in their own family, so a
    large block of trend tests does not swamp a small block of contradictions.
    """
    p = np.asarray(p_values, dtype=float)
    labels = np.asarray(families)
    if labels.shape != p.shape:
        raise ValueError("families must have one label per p-value")

    adjusted = np.full(p.shape, np.nan)
    significant = np.zeros(p.shape, dtype=bool)
    for label in np.unique(labels):
        mask = labels == label
        adjusted[mask], significant[mask] = benjamini_hochberg(p[mask], alpha)
    return adjusted, significant
