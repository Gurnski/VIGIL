using System.Text.Json;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Controls.Evidence
{
    /// <summary>
    ///  Everything the results say about one finding: its figures with their uncertainty, the
    ///  series behind it, why it ranks where it does, what a re-test on a second product found,
    ///  the method and its settings, the sources, the limitations and the findings it links to.
    ///
    ///  It only lays out what the analysis service wrote. No number here is worked out by the
    ///  app, and nothing the service said about the finding's limits is left out. The control
    ///  sets its own height, so put it in a scrolling panel.
    /// </summary>
    [System.ComponentModel.DesignerCategory("Code")]
    public class EvidenceView : UserControl
    {
        private const TextFormatFlags Wrapped =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
            | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private readonly TimeSeriesChart firstChart = new() { Visible = false };
        private readonly TimeSeriesChart secondChart = new() { Visible = false };
        private readonly VigilButton showOnMap = new() { Variant = ButtonVariant.Secondary, IconGlyph = Icons.Globe, Text = "Show on the map" };
        private readonly VigilButton ask = new() { Variant = ButtonVariant.Secondary, IconGlyph = Icons.Chat, Text = "Ask the Detective" };
        private readonly VigilButton recompute = new() { Variant = ButtonVariant.Ghost, IconGlyph = Icons.Refresh, Text = "Recompute" };
        private readonly VigilButton copy = new() { Variant = ButtonVariant.Ghost, IconGlyph = Icons.Copy, Text = "Copy as text" };
        private readonly List<(Rectangle Bounds, Action Action)> links = new();
        private readonly ToolTip tip = new() { InitialDelay = 400 };

        private Finding? finding;
        private string message = "Choose a finding to see its evidence.";
        private List<(string Title, Series? Series, double? Slope)> charts = new();
        private string seriesNote = "";
        private string recomputed = "";
        private bool synthetic;
        private double threshold = 0.05;
        private Func<string, Finding?> lookup = id => null;
        private int hoveredLink = -1;

        /// <summary>Raised by "Show on the map".</summary>
        public event EventHandler<Finding>? ShowOnMapClicked;

        /// <summary>Raised by "Ask the Detective".</summary>
        public event EventHandler<Finding>? AskClicked;

        /// <summary>Raised by "Recompute", which asks the service to work the finding's cell out again from the gridded data.</summary>
        public event EventHandler<Finding>? RecomputeClicked;

        /// <summary>Raised by "Copy as text", for the page to put the finding's written-out evidence on the clipboard.</summary>
        public event EventHandler<Finding>? CopyClicked;

        /// <summary>Raised with a finding's id when a linked finding is clicked.</summary>
        public event EventHandler<string>? FindingClicked;

        public EvidenceView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;

            showOnMap.Click += (s, e) => { if (finding != null) ShowOnMapClicked?.Invoke(this, finding); };
            ask.Click += (s, e) => { if (finding != null) AskClicked?.Invoke(this, finding); };
            recompute.Click += (s, e) => { if (finding != null) RecomputeClicked?.Invoke(this, finding); };
            copy.Click += (s, e) => { if (finding != null) CopyClicked?.Invoke(this, finding); };
            tip.SetToolTip(copy, "Copies everything on this page as plain text, with every figure unrounded, for a report or an email.");
            tip.SetToolTip(recompute, "Asks the analysis service to work this cell out again from the gridded data, and sets the result beside the stored one.");
            Controls.AddRange(new Control[] { firstChart, secondChart, showOnMap, ask, recompute, copy });
            ShowButtons(false);
        }

        protected override Size DefaultSize => Theme.Scaled(720, 400);

        /// <summary>The finding on show, or null.</summary>
        [System.ComponentModel.Browsable(false)]
        public Finding? Finding => finding;

        /// <summary>Shows a line of text in place of a finding.</summary>
        public void ShowMessage(string text)
        {
            finding = null;
            message = text;
            charts.Clear();
            recomputed = "";
            ShowButtons(false);
            ShowCharts();
            Rearrange();
        }

        /// <summary>
        ///  Shows a finding. <paramref name="series"/> is every series the results hold for it:
        ///  any it needs and does not find there is said to be missing, in the words of
        ///  <paramref name="missingSeries"/>. <paramref name="find"/> looks up a linked finding by id.
        /// </summary>
        public void ShowFinding(Finding shown, IReadOnlyList<Series> series, RunRecord? run, bool isSynthetic, string missingSeries, Func<string, Finding?> find)
        {
            finding = shown;
            synthetic = isSynthetic;
            threshold = run?.Settings.Threshold ?? 0.05;
            lookup = find;
            recomputed = "";
            seriesNote = missingSeries;
            charts = ChartsFor(shown, series);
            ShowButtons(true);
            recompute.Visible = shown.Kind == FindingKind.Trend;
            ShowCharts();
            Rearrange();
        }

        /// <summary>Shows a line under the buttons about the finding on show, such as that its text was copied.</summary>
        public void ShowNote(Finding about, string text) => ShowRecomputed(about, text);

        /// <summary>Sets the service's fresh result for the finding on show beside its stored one, or says why there is none.</summary>
        public void ShowRecomputed(Finding about, string text)
        {
            if (finding?.Id == about.Id)
            {
                recomputed = text;
                Rearrange();
            }
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        private void ShowButtons(bool visible)
        {
            showOnMap.Visible = ask.Visible = recompute.Visible = copy.Visible = visible;
        }

        // ----- The series each kind of finding draws -----

        private static List<(string Title, Series? Series, double? Slope)> ChartsFor(Finding shown, IReadOnlyList<Series> series)
        {
            Series? Find(string cellId, string variable) =>
                series.FirstOrDefault(candidate => candidate.CellId == cellId && candidate.Variable == variable);

            var result = new List<(string, Series?, double?)>();
            if (shown.Kind == FindingKind.Contradiction && shown.Contradiction is Contradiction pair)
            {
                string name = FindingsList.LabelText(pair.Variable);
                result.Add((name + " in this cell, " + FindingsList.PlaceText(shown.Cell), Find(shown.Cell.Id, pair.Variable), pair.SlopePerDecade));
                result.Add((name + " in the paired cell, " + FindingsList.PlaceText(pair.PairedCell), Find(pair.PairedCell.Id, pair.Variable), pair.PairedSlopePerDecade));
                return result;
            }
            foreach (var variable in shown.Variables)
            {
                double? slope = shown.Kind == FindingKind.Trend ? shown.Result?.SlopePerDecade : null;
                result.Add((variable.Name.Length > 0 ? variable.Name : FindingsList.LabelText(variable.Id), Find(shown.Cell.Id, variable.Id), slope));
            }
            return result;
        }

        private void ShowCharts()
        {
            var controls = new[] { firstChart, secondChart };
            for (int i = 0; i < controls.Length; i++)
            {
                var chart = controls[i];
                var series = i < charts.Count ? charts[i].Series : null;
                var dates = new List<DateTime>();
                var values = new List<double>();
                if (series != null)
                {
                    for (int month = 0; month < series.Dates.Count && month < series.Anomaly.Count; month++)
                    {
                        if (series.Anomaly[month] is double value)
                        {
                            dates.Add(series.Dates[month].ToDateTime(TimeOnly.MinValue));
                            values.Add(value);
                        }
                    }
                }

                chart.Visible = values.Count > 1;
                if (!chart.Visible)
                {
                    continue;
                }
                double largest = values.Max(Math.Abs);
                chart.Units = series!.Units;
                chart.ValueFormat = largest >= 100 ? "0" : largest >= 10 ? "0.0" : largest >= 1 ? "0.00" : "0.000";
                chart.ReportedSlopePerDecade = charts[i].Slope;
                chart.ShowTrendLine = charts[i].Slope != null;
                if (finding?.Kind == FindingKind.ChangePoint && finding.ChangePoint is ChangePoint step && i == 0)
                {
                    chart.SetChangePoint(step.Date.ToDateTime(TimeOnly.MinValue), step.DateLow.ToDateTime(TimeOnly.MinValue),
                        step.DateHigh.ToDateTime(TimeOnly.MinValue), step.LevelBefore, step.LevelAfter);
                }
                else
                {
                    chart.ClearChangePoint();
                }
                chart.SetData(dates, values);
            }
        }

        // ----- Layout -----

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

        /// <summary>A term on the left and its value, wrapped, on the right.</summary>
        private int Row(Graphics? g, string term, string value, int y, int width, Color? valueColour = null)
        {
            int termWidth = Px(170);
            int height = Math.Max(Px(18), TextHeight(value, Theme.Body, width - termWidth));
            if (g != null)
            {
                Draw.Text(g, term, Theme.Body, new Rectangle(0, y, termWidth - Px(Theme.Space3), Px(18)), Theme.TextMuted);
                Draw.Text(g, value, Theme.Body, new Rectangle(termWidth, y, width - termWidth, height), valueColour ?? Theme.TextPrimary, Wrapped);
            }
            return y + height + Px(Theme.Space1);
        }

        /// <summary>Lays the view out from the top, drawing as it goes when given a surface, and returns the height it needs.</summary>
        private int Arrange(Graphics? g)
        {
            int width = Width;
            int y = 0;
            links.Clear();

            if (finding == null)
            {
                return Paragraph(g, message, Theme.Body, Theme.TextSecondary, Px(Theme.Space2), width) + Px(Theme.Space4);
            }
            var shown = finding;

            // What it is, and where.
            string kicker = "#" + Numbers.Count(shown.Rank) + "  " + FindingsList.LabelText(shown.Kind) + ", " + FindingsList.PlaceText(shown.Cell)
                + ", cell " + shown.Cell.Id + ", " + Numbers.Month(shown.Period.Start) + " to " + Numbers.Month(shown.Period.End);
            y = Paragraph(g, kicker, Theme.Caption, Theme.TextSecondary, y, width) + Px(Theme.Space2);
            y = Paragraph(g, shown.Summary, Theme.Title, Theme.TextPrimary, y, width) + Px(Theme.Space3);

            y = Badges(g, shown, y, width) + Px(Theme.Space3);

            int buttonHeight = Px(Theme.ControlHeight);
            int x = 0;
            foreach (var (button, buttonWidth) in new[] { (showOnMap, 168), (ask, 172), (recompute, 128), (copy, 140) })
            {
                var bounds = new Rectangle(x, y, Px(buttonWidth), buttonHeight);
                if (button.Bounds != bounds)
                {
                    button.Bounds = bounds;
                }
                if (button.Visible)
                {
                    x += Px(buttonWidth) + Px(Theme.Space2);
                }
            }
            y += buttonHeight + Px(Theme.Space2);
            if (recomputed.Length > 0)
            {
                y = Paragraph(g, recomputed, Theme.Body, Theme.TextSecondary, y + Px(Theme.Space1), width) + Px(Theme.Space1);
            }

            y = Figures(g, shown, y + Px(Theme.Space3), width);

            // The series.
            y = Heading(g, "The series", y, width);
            var chartControls = new[] { firstChart, secondChart };
            bool anyChart = false, anyMissing = false;
            for (int i = 0; i < chartControls.Length; i++)
            {
                if (i >= charts.Count)
                {
                    continue;
                }
                string unit = charts[i].Series?.Units is string u && u.Length > 0 ? ", " + u : "";
                y = Paragraph(g, charts[i].Title + ": monthly anomaly" + unit, Theme.CaptionStrong, Theme.TextSecondary, y, width) + Px(Theme.Space1);
                if (chartControls[i].Visible)
                {
                    var bounds = new Rectangle(0, y, width, Px(230));
                    if (chartControls[i].Bounds != bounds)
                    {
                        chartControls[i].Bounds = bounds;
                    }
                    y += bounds.Height + Px(Theme.Space3);
                    anyChart = true;
                }
                else
                {
                    y = Paragraph(g, seriesNote, Theme.Body, Theme.TextMuted, y, width) + Px(Theme.Space3);
                    anyMissing = true;
                }
            }
            if (anyChart)
            {
                string guide = "Each value is the month's value minus that calendar month's mean over the period.";
                if (shown.Kind is FindingKind.Trend or FindingKind.Contradiction)
                {
                    guide += " The dashed line has the slope the analysis service reported and is placed on the series as a guide for the eye.";
                }
                else if (shown.Kind == FindingKind.ChangePoint)
                {
                    guide += " The upright line is the change point, with the range its date may lie in, and the dashed lines are the level before and after.";
                }
                y = Paragraph(g, guide, Theme.Caption, Theme.TextMuted, y, width);
            }
            else if (!anyMissing)
            {
                y = Paragraph(g, seriesNote, Theme.Body, Theme.TextMuted, y, width);
            }

            if (shown.Cascade is Cascade lagged && lagged.CorrelationsByLag is { Count: > 1 } byLag)
            {
                y = Heading(g, "Correlation at each lag", y, width);
                y = LagBars(g, lagged, byLag, y, width);
            }

            // Why it ranks here.
            y = Heading(g, "Why it ranks here", y, width);
            y = ScoreBars(g, shown.Score, y, width);

            if (shown.Standing.Retest is Retest retest)
            {
                y = Heading(g, "Re-tested on a second product", y, width);
                y = RetestRows(g, shown, retest, y, width);
            }

            y = Heading(g, "Method", y, width);
            // The analysis names some tests in words and some as an id with underscores. Both read as words here.
            y = Row(g, "Test", shown.Method.Test.Contains('_') ? FindingsList.LabelText(shown.Method.Test) : shown.Method.Test, y, width);
            foreach (var parameter in shown.Method.Parameters)
            {
                y = Row(g, ParameterLabels.GetValueOrDefault(parameter.Key, FindingsList.LabelText(parameter.Key)), ParameterText(parameter.Key, parameter.Value), y, width);
            }
            y = Row(g, "Regridding", shown.Preprocessing.Regridding, y, width);
            y = Row(g, "Aggregation", shown.Preprocessing.Aggregation, y, width);
            y = Row(g, "Anomaly", shown.Preprocessing.Anomaly, y, width);
            y = Row(g, "Missing data", shown.Preprocessing.MissingDataRule, y, width);

            y = Heading(g, shown.Variables.Count == 1 ? "Source" : "Sources", y, width);
            foreach (var variable in shown.Variables)
            {
                string source = variable.Mission + ", product " + variable.Product
                    + (string.IsNullOrEmpty(variable.Field) ? "" : ", field " + variable.Field)
                    + (string.IsNullOrEmpty(variable.NativeResolution) ? "" : ". Native resolution: " + variable.NativeResolution);
                y = Row(g, variable.Name.Length > 0 ? variable.Name : FindingsList.LabelText(variable.Id), source, y, width);
                y = Link(g, variable.SourceUrl, variable.SourceUrl, y, width);
            }
            // Not a source of the finding: a way to look at the place itself.
            y = Row(g, "The place itself", "NASA Worldview shows satellite imagery of this cell for any day. It is for looking at the place, and is no part of the evidence.", y, width);
            y = Link(g, "Open this cell in NASA Worldview", VIGIL.Data.Worldview.Link(shown), y, width);

            y = Heading(g, "What this cannot say", y, width);
            if (shown.Standing.Limitations.Count == 0)
            {
                y = Paragraph(g, "The results list no limitations for this finding.", Theme.Body, Theme.TextSecondary, y, width);
            }
            foreach (string limitation in shown.Standing.Limitations)
            {
                y = Paragraph(g, limitation, Theme.Body, Theme.TextPrimary, y, width) + Px(Theme.Space2);
            }
            if (shown.Contradiction is Contradiction contradiction)
            {
                foreach (string confounder in contradiction.Confounders)
                {
                    y = Paragraph(g, confounder, Theme.Body, Theme.TextPrimary, y, width) + Px(Theme.Space2);
                }
            }

            if (shown.Links.Count > 0)
            {
                y = Heading(g, "Linked findings", y, width);
                foreach (var link in shown.Links)
                {
                    y = LinkedFinding(g, link, y, width);
                }
            }
            return y + Px(Theme.Space6);
        }

        // ----- The pieces -----

        private int Badges(Graphics? g, Finding shown, int y, int width)
        {
            var badges = new List<(string Text, Color Colour)>
            {
                shown.Significant ? ("Significant after correction", Theme.TextPrimary) : ("Not significant", Theme.TextSecondary)
            };
            if (shown.Standing.Retest is Retest retest)
            {
                // A re-test is never shown without the product it was made on.
                string product = retest.Product.Mission;
                badges.Add(retest.Outcome switch
                {
                    Retest.Confirmed => ("Re-tested: confirmed on " + product, Theme.Positive),
                    Retest.SameDirection => ("Exploratory: " + product + " leans the same way, not significant", Theme.TextSecondary),
                    Retest.Opposite => ("Caution: " + product + " trends the other way", Theme.Warning),
                    _ => ("Exploratory: " + product + " has too little data here", Theme.TextSecondary)
                });
            }
            else if (shown.Standing.Status == "retested")
            {
                // Results that call a finding re-tested must say on what. These do not, so the badge says that.
                badges.Add(("Re-tested, on a product these results do not name", Theme.TextSecondary));
            }
            else
            {
                badges.Add((FindingsList.LabelText(shown.Standing.Status), Theme.TextSecondary));
            }
            if (synthetic)
            {
                badges.Add(("Synthetic fixture", Theme.Warning));
            }

            int x = 0;
            int height = Px(22);
            foreach (var (text, colour) in badges)
            {
                int textWidth = TextRenderer.MeasureText(text, Theme.Caption, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                int badgeWidth = textWidth + Px(Theme.Space4);
                if (x > 0 && x + badgeWidth > width)
                {
                    x = 0;
                    y += height + Px(Theme.Space1);
                }
                if (g != null)
                {
                    var box = new RectangleF(x + 0.5f, y + 0.5f, badgeWidth - 1f, height - 1f);
                    Draw.RoundedBox(g, box, Px(Theme.RadiusSmall), Color.Transparent, Draw.Blend(Theme.Border, colour, 0.45f));
                    Draw.Text(g, text, Theme.Caption, new Rectangle(x, y, badgeWidth, height), colour,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
                x += badgeWidth + Px(Theme.Space2);
            }
            return y + height;
        }

        /// <summary>The headline figures for the finding's kind: three across, or stacked when the view is narrow.</summary>
        private int Figures(Graphics? g, Finding shown, int y, int width)
        {
            var figures = new List<(string Caption, string Value, string Note)>();
            string months = Numbers.Count(shown.Preprocessing.MonthsPresent) + " of " + Numbers.Count(shown.Preprocessing.MonthsExpected) + " months";
            string coverage = "Coverage " + Numbers.Percent(shown.Preprocessing.Coverage);

            if (shown.Kind == FindingKind.Trend && shown.Result is TrendResult result)
            {
                figures.Add(("Trend", Numbers.Signed(result.SlopePerDecade) + " " + result.Units,
                    Numbers.Percent(result.CiLevel) + " interval " + Numbers.Signed(result.CiLow) + " to " + Numbers.Signed(result.CiHigh)));
                figures.Add(("p-value after correction", Numbers.P(result.PAdjusted),
                    "Before correction " + Numbers.P(result.PValue) + ". The threshold is " + Numbers.P(threshold) + "."));
                figures.Add(("Data", months, coverage));
            }
            else if (shown.Kind == FindingKind.ChangePoint && shown.ChangePoint is ChangePoint step)
            {
                figures.Add(("Shift in level", Numbers.Signed(step.Shift) + " " + step.Units,
                    "From " + Numbers.Signed(step.LevelBefore) + " to " + Numbers.Signed(step.LevelAfter)));
                figures.Add(("When", Numbers.Month(step.Date), "Between " + Numbers.Month(step.DateLow) + " and " + Numbers.Month(step.DateHigh)));
                figures.Add(("Data", months, coverage));
            }
            else if (shown.Kind == FindingKind.Cascade && shown.Cascade is Cascade cascade)
            {
                string lag = cascade.LagMonths == 1 ? "1 month" : Numbers.Count(cascade.LagMonths) + " months";
                figures.Add(("Follows by", lag, FindingsList.LabelText(cascade.FollowingVariable) + " after " + FindingsList.LabelText(cascade.LeadingVariable).ToLowerInvariant()
                    + ". An association, not a cause."));
                figures.Add(("Correlation at that lag", Numbers.Signed(cascade.Correlation),
                    "p after correction " + Numbers.P(cascade.PAdjusted) + ", before " + Numbers.P(cascade.PValue)));
                figures.Add(("Data", months, "Months where both variables have a value"));
            }
            else if (shown.Kind == FindingKind.Contradiction && shown.Contradiction is Contradiction pair)
            {
                figures.Add(("This cell", Numbers.Signed(pair.SlopePerDecade) + " " + pair.Units, FindingsList.PlaceText(shown.Cell)));
                figures.Add(("Paired cell", Numbers.Signed(pair.PairedSlopePerDecade) + " " + pair.Units,
                    FindingsList.PlaceText(pair.PairedCell) + (pair.DistanceKm is double km ? ", " + Numbers.Count((int)Math.Round(km)) + " km away" : "")));
                figures.Add(("Data", months, coverage));
            }
            if (figures.Count == 0)
            {
                return y;
            }

            bool stacked = width < Px(560);
            int gap = Px(Theme.Space5);
            int column = stacked ? width : (width - gap * (figures.Count - 1)) / figures.Count;
            int tallest = 0;
            for (int i = 0; i < figures.Count; i++)
            {
                int left = stacked ? 0 : i * (column + gap);
                int top = y;
                int bottom = top;
                if (g != null)
                {
                    Draw.Caption(g, figures[i].Caption, new Rectangle(left, top, column, Px(16)), Theme.TextSecondary);
                }
                bottom += Px(16 + Theme.Space1);
                int valueHeight = TextHeight(figures[i].Value, Theme.Heading, column);
                if (g != null)
                {
                    Draw.Text(g, figures[i].Value, Theme.Heading, new Rectangle(left, bottom, column, valueHeight), Theme.TextPrimary, Wrapped);
                }
                bottom += valueHeight + Px(Theme.Space1);
                int noteHeight = TextHeight(figures[i].Note, Theme.Caption, column);
                if (g != null)
                {
                    Draw.Text(g, figures[i].Note, Theme.Caption, new Rectangle(left, bottom, column, noteHeight), Theme.TextMuted, Wrapped);
                }
                bottom += noteHeight;
                if (stacked)
                {
                    y = bottom + Px(Theme.Space3);
                }
                tallest = Math.Max(tallest, bottom - top);
            }
            return stacked ? y : y + tallest;
        }

        private int ScoreBars(Graphics? g, Score score, int y, int width)
        {
            var parts = new (string Label, double Value, string Meaning)[]
            {
                ("Effect size", score.EffectSize, "How large the change is, against the series' own noise"),
                ("Certainty", score.Certainty, "How tight the estimate is"),
                ("Coverage", score.Coverage, "How complete the data is"),
                ("Robustness", score.Robustness, "How well it survives a second test"),
                ("Cross-system", score.CrossSystem, "How many other variables or findings it connects to")
            };
            int label = Px(120);
            int bar = Math.Min(Px(200), Math.Max(Px(60), width - label - Px(64) - Px(260)));
            foreach (var (name, value, meaning) in parts)
            {
                if (g != null)
                {
                    Draw.Text(g, name, Theme.Body, new Rectangle(0, y, label, Px(20)), Theme.TextPrimary);
                    var track = new Rectangle(label, y + Px(7), bar, Px(6));
                    Draw.RoundedBox(g, track, Px(3), Theme.SurfaceRaised);
                    int filled = (int)Math.Round(bar * Math.Clamp(value, 0, 1));
                    if (filled > 0)
                    {
                        Draw.RoundedBox(g, new Rectangle(track.X, track.Y, filled, track.Height), Px(3), Theme.Series);
                    }
                    Draw.Text(g, value.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("en-GB")), Theme.Numeric,
                        new Rectangle(label + bar + Px(Theme.Space3), y, Px(44), Px(20)), Theme.TextPrimary);
                    Draw.Text(g, meaning, Theme.Caption, new Rectangle(label + bar + Px(64), y, Math.Max(0, width - label - bar - Px(64)), Px(20)), Theme.TextMuted);
                }
                y += Px(24);
            }
            return Paragraph(g, "Total " + score.Total.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("en-GB"))
                + ". The score orders findings for a closer look. It is not a measure of proof.", Theme.Caption, Theme.TextMuted, y + Px(Theme.Space1), width);
        }

        /// <summary>
        ///  The correlation the service found at every lag it tried, as bars either side of zero.
        ///  The lag the finding reports is the bar in the text colour.
        /// </summary>
        private int LagBars(Graphics? g, Cascade cascade, IReadOnlyList<double> byLag, int y, int width)
        {
            int plotHeight = Px(120);
            int labelHeight = Px(20);
            int axis = Px(44);
            int slot = Math.Min(Px(72), (width - axis) / byLag.Count);
            int bar = Math.Max(Px(8), slot * 3 / 5);
            int middle = y + plotHeight / 2;
            if (g != null)
            {
                var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
                foreach (var (value, text) in new[] { (1.0, "+1"), (0.0, "0"), (-1.0, "−1") })
                {
                    int lineY = middle - (int)Math.Round(value * plotHeight / 2);
                    Draw.HorizontalLine(g, axis, axis + slot * byLag.Count, lineY, value == 0 ? Theme.Border : Theme.GridLine);
                    Draw.Text(g, text, Theme.NumericSmall, new Rectangle(0, lineY - Px(8), axis - Px(Theme.Space2), Px(16)), Theme.TextMuted,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
                for (int lag = 0; lag < byLag.Count; lag++)
                {
                    double value = Math.Clamp(byLag[lag], -1, 1);
                    int length = (int)Math.Round(Math.Abs(value) * plotHeight / 2);
                    int left = axis + lag * slot + (slot - bar) / 2;
                    var bounds = value >= 0 ? new Rectangle(left, middle - length, bar, length) : new Rectangle(left, middle, bar, length);
                    if (length > 0)
                    {
                        using var brush = new SolidBrush(lag == cascade.LagMonths ? Theme.TextPrimary : Theme.Series);
                        g.FillRectangle(brush, bounds);
                    }
                    Draw.Text(g, lag.ToString(british), Theme.NumericSmall, new Rectangle(axis + lag * slot, y + plotHeight + Px(4), slot, labelHeight),
                        lag == cascade.LagMonths ? Theme.TextPrimary : Theme.TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
            y += plotHeight + labelHeight + Px(Theme.Space2);
            return Paragraph(g, "Months by which " + FindingsList.LabelText(cascade.FollowingVariable).ToLowerInvariant() + " trails "
                + FindingsList.LabelText(cascade.LeadingVariable).ToLowerInvariant() + ". The lighter bar is the lag the finding reports. "
                + "A link in time between two series is an association, not a cause.", Theme.Caption, Theme.TextMuted, y, width);
        }

        private int RetestRows(Graphics? g, Finding shown, Retest retest, int y, int width)
        {
            var product = retest.Product;
            y = Row(g, "Second product", product.Mission + ", product " + product.Product
                + (string.IsNullOrEmpty(product.Field) ? "" : ", field " + product.Field)
                + (string.IsNullOrEmpty(product.NativeResolution) ? "" : ". " + product.NativeResolution), y, width);
            y = Link(g, product.SourceUrl, product.SourceUrl, y, width);
            var (outcome, colour) = retest.Outcome switch
            {
                Retest.Confirmed => ("Confirmed: the second record has a significant trend in the same direction.", Theme.Positive),
                Retest.SameDirection => ("Not confirmed: the second record leans the same way, but its trend is not significant on its own.", Theme.TextPrimary),
                Retest.Opposite => ("Not confirmed: the second record trends the other way.", Theme.Warning),
                _ => ("Could not be re-tested: the second record has too little data in this cell.", Theme.TextPrimary)
            };
            y = Row(g, "Outcome", outcome, y, width, colour);
            if (retest.SlopePerDecade is double slope && retest.PValue is double p)
            {
                string interval = retest.CiLow is double low && retest.CiHigh is double high
                    ? ", " + Numbers.Percent(retest.CiLevel ?? 0.95) + " interval " + Numbers.Signed(low) + " to " + Numbers.Signed(high)
                    : "";
                y = Row(g, "Its own trend", Numbers.Signed(slope) + " " + retest.Units + interval, y, width);
                y = Row(g, "Its own p-value", Numbers.P(p) + " (one test, not corrected for many tests)", y, width);
                // The two products may be in different units, so their sizes are set side by side only when the units match.
                if (shown.Result is TrendResult own && own.Units != retest.Units)
                {
                    y = Row(g, "", "The second product is in other units, so only its direction and significance are compared with the finding's.", y, width, Theme.TextMuted);
                }
            }
            if (retest.MonthsPresent is int present && retest.MonthsExpected is int expected)
            {
                y = Row(g, "Its data", Numbers.Count(present) + " of " + Numbers.Count(expected) + " months", y, width);
            }
            return y;
        }

        // The settings the analysis writes with a test, in words. One it adds later shows under its own name.
        private static readonly Dictionary<string, string> ParameterLabels = new()
        {
            ["alpha"] = "Significance level",
            ["ci_level"] = "Confidence level",
            ["max_lag_months"] = "Autocorrelation lag, months",
            ["max_lag_years"] = "Autocorrelation lag, years",
            ["variance_inflation"] = "Variance inflation",
            ["variance_correction_floored"] = "Variance correction floored",
            ["slope_estimator"] = "Slope estimator",
            ["robustness_test"] = "Second test",
            ["robustness_p_value"] = "Second test's p-value",
            ["multiple_testing"] = "Correction for many tests",
            ["threshold"] = "Threshold"
        };

        private static readonly Dictionary<string, string> ParameterValues = new()
        {
            ["sen"] = "Sen's slope",
            ["benjamini_hochberg"] = "Benjamini-Hochberg false discovery rate"
        };

        private static string ParameterText(string key, JsonElement value)
        {
            var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
            switch (value.ValueKind)
            {
                case JsonValueKind.Number:
                    if (value.TryGetInt64(out long whole))
                    {
                        return whole.ToString(british);
                    }
                    double number = value.GetDouble();
                    return key.Contains("p_value", StringComparison.Ordinal) ? Numbers.P(number) : number.ToString("0.####", british);
                case JsonValueKind.True:
                    return "Yes";
                case JsonValueKind.False:
                    return "No";
                case JsonValueKind.String:
                    string text = value.GetString() ?? "";
                    return ParameterValues.GetValueOrDefault(text, text.Contains('_') ? FindingsList.LabelText(text) : text);
                default:
                    return value.ToString();
            }
        }

        /// <summary>A web address that opens in the browser when clicked.</summary>
        private int Link(Graphics? g, string text, string address, int y, int width)
        {
            if (string.IsNullOrEmpty(address))
            {
                return y;
            }
            int left = Px(170);
            int height = Px(18);
            var bounds = new Rectangle(left, y, Math.Min(width - left, TextRenderer.MeasureText(text, Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width + Px(2)), height);
            int index = links.Count;
            links.Add((bounds, () => Open(address)));
            if (g != null)
            {
                Draw.Text(g, text, Theme.Body, bounds, index == hoveredLink ? Theme.AccentHover : Theme.Accent);
                Draw.HorizontalLine(g, bounds.Left, bounds.Right - 1, bounds.Bottom - Px(2), Draw.WithAlpha(Theme.Accent, index == hoveredLink ? 200 : 90));
            }
            return y + height + Px(Theme.Space2);
        }

        private int LinkedFinding(Graphics? g, FindingLink link, int y, int width)
        {
            var other = lookup(link.FindingId);
            string detail = FindingsList.LabelText(link.Kind)
                + (link.LagMonths is int lag ? ", " + (lag == 1 ? "1 month" : lag.ToString(System.Globalization.CultureInfo.InvariantCulture) + " months") + " apart" : "")
                + (link.Correlation is double r ? ", correlation " + Numbers.Signed(r) : "")
                + (link.CellId is string cell ? ", cell " + cell : "");
            string title = other?.Summary ?? link.FindingId;
            int height = Px(46);
            var row = new Rectangle(0, y, width, height - Px(Theme.Space1));
            int index = links.Count;
            links.Add((row, () => FindingClicked?.Invoke(this, link.FindingId)));
            if (g != null)
            {
                if (index == hoveredLink)
                {
                    Draw.RoundedBox(g, row, Px(Theme.RadiusSmall), Theme.SurfaceRaised);
                }
                int pad = Px(Theme.Space3);
                Draw.Text(g, title, Theme.Body, new Rectangle(row.X + pad, row.Y + Px(4), row.Width - pad * 2, Px(18)), Theme.TextPrimary);
                Draw.Text(g, detail + (other == null ? ". Not in these results." : ""), Theme.Caption,
                    new Rectangle(row.X + pad, row.Y + Px(23), row.Width - pad * 2, Px(16)), Theme.TextMuted);
            }
            return y + height;
        }

        private static void Open(string address)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // No browser to open it with. The address is on screen to copy.
            }
        }

        private int LinkAt(Point location)
        {
            for (int i = 0; i < links.Count; i++)
            {
                if (links[i].Bounds.Contains(location))
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = LinkAt(e.Location);
            if (hit != hoveredLink)
            {
                hoveredLink = hit;
                Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredLink >= 0)
            {
                hoveredLink = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int hit = LinkAt(e.Location);
            if (e.Button == MouseButtons.Left && hit >= 0)
            {
                links[hit].Action();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
