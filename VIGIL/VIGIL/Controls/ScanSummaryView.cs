using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  What a scan covered and found, variable by variable, read straight from the results
    ///  document: the source of each variable, how many of its tests were significant, which
    ///  way they point, and, when the scan was re-tested on a second product, how that came
    ///  out beside the rate to hold it against. Under the variables, the settings the scan
    ///  was run with and the tests it skipped.
    ///
    ///  The only arithmetic here is counting findings and the share one count is of another.
    ///  The control sets its own height, so put it in a scrolling panel.
    /// </summary>
    [System.ComponentModel.DesignerCategory("Code")]
    public class ScanSummaryView : UserControl
    {
        private const TextFormatFlags Wrapped =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
            | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        private const TextFormatFlags RightAligned =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private readonly List<(Rectangle Bounds, string Variable)> rows = new();
        private ResultsDocument? results;
        private int hovered = -1;

        /// <summary>Raised with a variable's id when its row is clicked.</summary>
        public event EventHandler<string>? VariableClicked;

        public ScanSummaryView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
        }

        protected override Size DefaultSize => Theme.Scaled(640, 400);

        public void SetResults(ResultsDocument document)
        {
            results = document;
            Rearrange();
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        private void Rearrange()
        {
            int height = Arrange(null);
            if (Height != height)
            {
                Height = height;
            }
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Rearrange();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Draw.Smooth(e.Graphics);
            Arrange(e.Graphics);
        }

        private static int TextHeight(string text, Font font, int width) =>
            TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), Wrapped).Height;

        private int Paragraph(Graphics? g, string text, Font font, Color colour, int y, int width, int left = 0)
        {
            if (text.Length == 0)
            {
                return y;
            }
            int height = TextHeight(text, font, width - left);
            if (g != null)
            {
                Draw.Text(g, text, font, new Rectangle(left, y, width - left, height), colour, Wrapped);
            }
            return y + height;
        }

        private int Heading(Graphics? g, string text, int y, int width)
        {
            y += Px(Theme.Space5);
            if (g != null)
            {
                Draw.HorizontalLine(g, 0, width - 1, y, Theme.Divider);
                Draw.Caption(g, text, new Rectangle(0, y + Px(Theme.Space3), width, Px(16)), Theme.TextSecondary);
            }
            return y + Px(Theme.Space3 + 16 + Theme.Space2);
        }

        private int Fact(Graphics? g, string term, string value, int y, int width)
        {
            int termWidth = Px(190);
            int height = Math.Max(Px(18), TextHeight(value, Theme.Body, width - termWidth));
            if (g != null)
            {
                Draw.Text(g, term, Theme.Body, new Rectangle(0, y, termWidth - Px(Theme.Space3), Px(18)), Theme.TextMuted);
                Draw.Text(g, value, Theme.Body, new Rectangle(termWidth, y, width - termWidth, height), Theme.TextPrimary, Wrapped);
            }
            return y + height + Px(Theme.Space1);
        }

        private int Arrange(Graphics? g)
        {
            int width = Width;
            int y = 0;
            rows.Clear();
            if (results == null)
            {
                return Px(Theme.Space4);
            }

            var run = results.Run;
            foreach (var variable in results.Variables)
            {
                y = VariableRow(g, variable, run, y, width);
            }

            if (run?.Retest is RunRetest retest && retest.ByVariable.Count > 0)
            {
                y = Heading(g, "How the re-test works", y, width);
                y = Paragraph(g, retest.Rule, Theme.Body, Theme.TextPrimary, y, width) + Px(Theme.Space2);
                y = Paragraph(g, "A confirmation counts for more where the second product is seldom significant by itself. That rate is "
                    + "given beside each variable above: where it is high, as it is for temperature, the second product would agree with "
                    + "almost any finding, and what it contradicts says more than what it confirms.", Theme.Body, Theme.TextSecondary, y, width);
            }

            if (run != null)
            {
                var settings = run.Settings;
                var counts = run.Counts;
                y = Heading(g, "The scan", y, width);
                y = Fact(g, "Period", Numbers.Month(settings.Period.Start) + " to " + Numbers.Month(settings.Period.End) + ", " + settings.Period.Step, y, width);
                y = Fact(g, "Grid", settings.Grid.CellDegrees.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) + "° cells, "
                    + Numbers.Count(counts.CellsTotal) + " in all, " + Numbers.Count(counts.CellsScanned) + " with at least one variable tested", y, width);
                y = Fact(g, "A cell is tested when", "at least " + Numbers.Percent(settings.MinCoverage) + " of its months have a value", y, width);
                y = Fact(g, "Significant when", "the p-value, after " + FindingsList.LabelText(settings.MultipleTesting).Replace("Benjamini hochberg", "Benjamini-Hochberg")
                    + " correction for many tests, is below " + Numbers.P(settings.Threshold), y, width);
                y = Fact(g, "Tests", Numbers.Count(counts.TestsRun) + " run of " + Numbers.Count(counts.TestsTotal) + " cell and variable pairs. "
                    + Numbers.Count(counts.TestsSignificant) + " significant, " + Numbers.Count(counts.TestsNotSignificant) + " not.", y, width);
                string skipped = string.Join(", ", counts.SkippedByReason.Where(pair => pair.Value > 0)
                    .Select(pair => Numbers.Count(pair.Value) + " " + SkipWords(pair.Key)));
                y = Fact(g, "Skipped", Numbers.Count(counts.TestsSkipped) + (skipped.Length > 0 ? ": " + skipped : ""), y, width);
                string kinds = string.Join(", ", counts.FindingsByKind.OrderByDescending(pair => pair.Value)
                    .Select(pair => Numbers.Count(pair.Value) + " " + FindingsList.LabelText(pair.Key).ToLowerInvariant() + (pair.Value == 1 ? "" : "s")));
                y = Fact(g, "Findings", Numbers.Count(counts.FindingsTotal) + (kinds.Length > 0 ? ": " + kinds : ""), y, width);
                y = Fact(g, "Run", run.Id + ", code " + run.CodeVersion + ", " + run.DurationSeconds.ToString("0", System.Globalization.CultureInfo.GetCultureInfo("en-GB")) + " seconds", y, width);
            }
            return y + Px(Theme.Space4);
        }

        private static string SkipWords(string reason) => reason switch
        {
            "ocean" => "over ocean",
            "ice" => "over ice",
            "coverage" => "with too little data",
            _ => reason
        };

        /// <summary>One variable: its source, its tests, which way its trends point, and its re-test.</summary>
        private int VariableRow(Graphics? g, Variable variable, RunRecord? run, int y, int width)
        {
            int top = y;
            int pad = Px(Theme.Space3);
            int inner = width - pad * 2;
            var counts = run?.Counts.ByVariable.FirstOrDefault(entry => entry.Variable == variable.Id);
            var trends = results!.Findings.Where(finding => finding.Kind == FindingKind.Trend && finding.Significant
                && finding.Variables.Count > 0 && finding.Variables[0].Id == variable.Id).ToList();
            int rising = trends.Count(finding => finding.Result?.Direction == "increasing");
            int falling = trends.Count(finding => finding.Result?.Direction == "decreasing");
            var retest = run?.Retest?.ByVariable.FirstOrDefault(entry => entry.Variable == variable.Id);

            // Measured first, so the highlight can be drawn behind the text.
            int height = Px(8 + 20 + 18 + 12 + 8);
            string retestText = "";
            if (retest != null)
            {
                retestText = "Re-tested on " + retest.Product.Mission + ": " + Numbers.Count(retest.Confirmed) + " of " + Numbers.Count(retest.Findings)
                    + " confirmed, " + Numbers.Count(retest.SameDirection) + " same direction but not significant, " + Numbers.Count(retest.Opposite)
                    + " opposite" + (retest.NotPossible > 0 ? ", " + Numbers.Count(retest.NotPossible) + " could not be tested" : "") + ". Where the scan found nothing, "
                    + retest.Product.Mission + " is significant in " + Numbers.Count(retest.OtherCellsSignificant) + " of " + Numbers.Count(retest.OtherCellsTested) + " cells.";
                height += TextHeight(retestText, Theme.Caption, inner) + Px(Theme.Space1);
            }
            var row = new Rectangle(0, top, width, height - Px(Theme.Space1));
            int index = rows.Count;
            rows.Add((row, variable.Id));

            if (g != null)
            {
                if (index == hovered)
                {
                    Draw.RoundedBox(g, row, Px(Theme.RadiusSmall), Theme.SurfaceRaised);
                }
                int line = top + Px(8);
                string name = variable.Name.Length > 0 ? variable.Name : FindingsList.LabelText(variable.Id);
                Draw.Text(g, name, Theme.BodyStrong, new Rectangle(pad, line, inner, Px(20)), Theme.TextPrimary);
                if (counts != null)
                {
                    Draw.Text(g, Numbers.Count(counts.TestsSignificant) + " of " + Numbers.Count(counts.TestsRun) + " tests significant",
                        Theme.Numeric, new Rectangle(pad, line, inner, Px(20)), Theme.TextPrimary, RightAligned);
                }
                line += Px(20);
                Draw.Text(g, variable.Mission + ", " + variable.Product + (string.IsNullOrEmpty(variable.NativeResolution) ? "" : ", " + variable.NativeResolution),
                    Theme.Caption, new Rectangle(pad, line, inner * 2 / 3, Px(18)), Theme.TextMuted);
                Draw.Text(g, Numbers.Count(rising) + " rising, " + Numbers.Count(falling) + " falling", Theme.NumericSmall,
                    new Rectangle(pad, line, inner, Px(18)), Theme.TextSecondary, RightAligned);
                line += Px(18 + 4);

                // The share of the tests run that were significant.
                var track = new Rectangle(pad, line, inner, Px(4));
                Draw.RoundedBox(g, track, Px(2), Theme.SurfaceRaised);
                if (counts != null && counts.TestsRun > 0)
                {
                    int filled = (int)Math.Round(inner * (double)counts.TestsSignificant / counts.TestsRun);
                    if (filled > 0)
                    {
                        Draw.RoundedBox(g, new Rectangle(track.X, track.Y, filled, track.Height), Px(2), Theme.Series);
                    }
                }
                line += Px(8 + 4);
                if (retestText.Length > 0)
                {
                    Draw.Text(g, retestText, Theme.Caption, new Rectangle(pad, line, inner, TextHeight(retestText, Theme.Caption, inner)), Theme.TextSecondary, Wrapped);
                }
            }
            return top + height;
        }

        private int RowAt(Point location)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Bounds.Contains(location))
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = RowAt(e.Location);
            if (hit != hovered)
            {
                hovered = hit;
                Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hovered >= 0)
            {
                hovered = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int hit = RowAt(e.Location);
            if (e.Button == MouseButtons.Left && hit >= 0)
            {
                VariableClicked?.Invoke(this, rows[hit].Variable);
            }
        }
    }
}
