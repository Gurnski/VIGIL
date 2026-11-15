using System.Globalization;
using System.Text;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>
    ///  A finding written out as plain text: what it is, its figures as the analysis service
    ///  stored them, what a re-test found, the method and its settings, the sources and every
    ///  limitation. It is the evidence view without the screen, for pasting into a report or
    ///  an email. Nothing is rounded, so the text can be checked against the results document,
    ///  and nothing is worked out here.
    /// </summary>
    public static class EvidenceReport
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string Write(Finding finding, RunRecord? run, bool synthetic)
        {
            var text = new StringBuilder();
            void Line(string line = "") => text.Append(line).Append("\r\n");
            string Number(double value) => value.ToString("R", Invariant);

            Line("VIGIL finding " + finding.Id);
            Line(finding.Summary);
            if (synthetic)
            {
                Line("These figures are from the synthetic fixture. They are made up.");
            }
            Line();
            Line("Kind: " + finding.Kind + ", rank " + finding.Rank.ToString(Invariant) + (finding.Significant ? ", significant after correction" : ", not significant"));
            Line("Cell: " + finding.Cell.Id + ", latitude " + Number(finding.Cell.LatMin) + " to " + Number(finding.Cell.LatMax)
                + ", longitude " + Number(finding.Cell.LonMin) + " to " + Number(finding.Cell.LonMax));
            Line("Period: " + finding.Period.Start.ToString("yyyy-MM", Invariant) + " to " + finding.Period.End.ToString("yyyy-MM", Invariant) + ", " + finding.Period.Step);
            if (run != null)
            {
                Line("Scan: " + run.Id + ", code " + run.CodeVersion + ", threshold " + Number(run.Settings.Threshold));
            }

            if (finding.Result is TrendResult trend)
            {
                Line();
                Line("Trend: " + Number(trend.SlopePerDecade) + " " + trend.Units + " (" + trend.Direction + ")");
                Line("Interval at " + Number(trend.CiLevel) + ": " + Number(trend.CiLow) + " to " + Number(trend.CiHigh));
                Line("p-value: " + Number(trend.PValue) + " before correction, " + Number(trend.PAdjusted) + " after");
            }
            if (finding.ChangePoint is ChangePoint step)
            {
                Line();
                Line("Change point: " + step.Date.ToString("yyyy-MM", Invariant) + ", between " + step.DateLow.ToString("yyyy-MM", Invariant) + " and " + step.DateHigh.ToString("yyyy-MM", Invariant));
                Line("Level: " + Number(step.LevelBefore) + " before, " + Number(step.LevelAfter) + " after, a shift of " + Number(step.Shift) + " " + step.Units);
            }
            if (finding.Cascade is Cascade cascade)
            {
                Line();
                Line("Cascade: " + cascade.FollowingVariable + " trails " + cascade.LeadingVariable + " by " + cascade.LagMonths.ToString(Invariant) + " month(s). An association, not a cause.");
                Line("Correlation at that lag: " + Number(cascade.Correlation) + ", p-value " + Number(cascade.PValue) + " before correction, " + Number(cascade.PAdjusted) + " after");
            }
            if (finding.Contradiction is Contradiction pair)
            {
                Line();
                Line("Contradiction in " + pair.Variable + ": " + Number(pair.SlopePerDecade) + " " + pair.Units + " in this cell, "
                    + Number(pair.PairedSlopePerDecade) + " in cell " + pair.PairedCell.Id
                    + (pair.DistanceKm is double km ? ", " + Number(km) + " km away" : ""));
            }

            Line();
            Line("Data: " + finding.Preprocessing.MonthsPresent.ToString(Invariant) + " of " + finding.Preprocessing.MonthsExpected.ToString(Invariant)
                + " months, coverage " + Number(finding.Preprocessing.Coverage));
            Line("Score: total " + Number(finding.Score.Total) + ", effect size " + Number(finding.Score.EffectSize) + ", certainty " + Number(finding.Score.Certainty)
                + ", coverage " + Number(finding.Score.Coverage) + ", robustness " + Number(finding.Score.Robustness) + ", cross-system " + Number(finding.Score.CrossSystem));

            if (finding.Standing.Retest is Retest retest)
            {
                Line();
                Line("Re-tested on " + retest.Product.Mission + ", product " + retest.Product.Product + " (" + retest.Product.SourceUrl + "): " + retest.Outcome);
                if (retest.SlopePerDecade is double slope && retest.PValue is double p)
                {
                    Line("Its own trend: " + Number(slope) + " " + retest.Units + ", p-value " + Number(p) + " (one test, not corrected for many tests)");
                }
            }
            else
            {
                Line();
                Line("Standing: " + finding.Standing.Status);
            }

            Line();
            Line("Method: " + finding.Method.Test);
            foreach (var parameter in finding.Method.Parameters)
            {
                Line("  " + parameter.Key + ": " + parameter.Value.ToString());
            }
            Line("Regridding: " + finding.Preprocessing.Regridding);
            Line("Aggregation: " + finding.Preprocessing.Aggregation);
            Line("Anomaly: " + finding.Preprocessing.Anomaly);
            Line("Missing data: " + finding.Preprocessing.MissingDataRule);

            Line();
            foreach (var variable in finding.Variables)
            {
                Line("Source for " + variable.Id + ": " + variable.Mission + ", product " + variable.Product
                    + (string.IsNullOrEmpty(variable.Field) ? "" : ", field " + variable.Field) + ", " + variable.SourceUrl);
            }

            Line();
            Line("What this cannot say:");
            foreach (string limitation in finding.Standing.Limitations)
            {
                Line("  " + limitation);
            }
            if (finding.Contradiction is Contradiction confounded)
            {
                foreach (string confounder in confounded.Confounders)
                {
                    Line("  " + confounder);
                }
            }

            if (finding.Links.Count > 0)
            {
                Line();
                Line("Linked findings: " + string.Join(", ", finding.Links.Select(link => link.FindingId)));
            }
            return text.ToString();
        }
    }
}
