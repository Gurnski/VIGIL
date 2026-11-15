using System.Globalization;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Controls.Zones
{
    /// <summary>
    ///  What the analysis service says about one zone: a row per variable with its trend over
    ///  the zone, a chart of the chosen variable's series, and how the figures were made and
    ///  what they cannot say. Every number shown is the service's; this control only lays
    ///  them out. It sets its own height, so put it in a scrolling panel.
    /// </summary>
    [System.ComponentModel.DesignerCategory("Code")]
    public class ZoneSummaryView : UserControl
    {
        private const TextFormatFlags Wrapped =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
            | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        private const TextFormatFlags RightAligned =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private const string Caveat = "The zone was chosen by eye and nothing is corrected for many tests, so read each p-value as exploratory.";

        private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

        private readonly TimeSeriesChart chart = new() { Visible = false };
        private readonly List<Rectangle> rowBounds = new();

        private string title = "";
        private string message = "";
        private ZoneAnalysis? analysis;
        private string? selectedVariable;
        private int hovered = -1;

        /// <summary>Raised with a variable's id when its row is clicked.</summary>
        public event EventHandler<string>? VariableClicked;

        public ZoneSummaryView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Controls.Add(chart);
        }

        protected override Size DefaultSize => Theme.Scaled(340, 120);

        /// <summary>The variable whose row is marked and whose series is charted.</summary>
        public string? SelectedVariable
        {
            get => selectedVariable;
            set
            {
                if (selectedVariable == value)
                {
                    return;
                }
                selectedVariable = value;
                ShowSeries();
                Rearrange();
            }
        }

        /// <summary>Shows a line of text under the zone's name: that the figures are on their way, or why there are none.</summary>
        public void ShowMessage(string zoneName, string text)
        {
            title = zoneName;
            message = text;
            analysis = null;
            ShowSeries();
            Rearrange();
        }

        /// <summary>Shows the service's figures for a zone.</summary>
        public void ShowAnalysis(string zoneName, ZoneAnalysis result)
        {
            title = zoneName;
            message = "";
            analysis = result;
            ShowSeries();
            Rearrange();
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        private ZoneSummary? Selected => analysis?.Summaries.FirstOrDefault(summary => summary.Variable == selectedVariable);

        /// <summary>Hands the chosen variable's anomalies to the chart, leaving out the months with no value.</summary>
        private void ShowSeries()
        {
            var series = Selected?.Series;
            var dates = new List<DateTime>();
            var values = new List<double>();
            if (series != null)
            {
                for (int i = 0; i < series.Dates.Count && i < series.Anomaly.Count; i++)
                {
                    if (series.Anomaly[i] is double value)
                    {
                        dates.Add(series.Dates[i].ToDateTime(TimeOnly.MinValue));
                        values.Add(value);
                    }
                }
            }

            chart.Visible = values.Count > 1;
            if (chart.Visible)
            {
                double largest = values.Max(Math.Abs);
                chart.Units = series!.Units;
                chart.ValueFormat = largest >= 100 ? "0" : largest >= 10 ? "0.0" : largest >= 1 ? "0.00" : "0.000";
                chart.SetData(dates, values);
            }
        }

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

        /// <summary>Lays the view out from the top, drawing as it goes when given a surface, and returns the height it needs.</summary>
        private int Arrange(Graphics? g)
        {
            int width = Width;
            int y = 0;
            rowBounds.Clear();

            if (g != null)
            {
                Draw.HorizontalLine(g, 0, width - 1, y, Theme.Divider);
            }
            y += Px(Theme.Space3);

            if (g != null)
            {
                Draw.Text(g, title, Theme.BodyStrong, new Rectangle(0, y, width, Px(20)), Theme.TextPrimary);
            }
            y += Px(22);

            if (analysis == null)
            {
                int messageHeight = TextHeight(message, Theme.Caption, width);
                if (g != null)
                {
                    Draw.Text(g, message, Theme.Caption, new Rectangle(0, y, width, messageHeight), Theme.TextSecondary, Wrapped);
                }
                chart.Visible = false;
                return y + messageHeight + Px(Theme.Space2);
            }

            if (g != null)
            {
                Draw.Text(g, ExtentText(analysis), Theme.Caption, new Rectangle(0, y, width, Px(16)), Theme.TextMuted);
            }
            y += Px(16 + Theme.Space1);

            // Said above the figures, where it is always in view: the full notes are further down, under the chart.
            y = Paragraph(g, Caveat, y, width, Theme.TextSecondary);

            // The heading for the right-hand side of the rows, so each row need not repeat "per decade".
            if (g != null)
            {
                string heading = "Trend per decade, and its " + IntervalLevel(analysis) + " interval";
                Draw.Text(g, heading, Theme.Caption, new Rectangle(0, y, width - Px(Theme.Space3), Px(16)), Theme.TextMuted, RightAligned);
            }
            y += Px(16 + Theme.Space1);

            // One row per variable: its name and trend, then the p-value and the interval.
            int rowHeight = Px(46);
            for (int i = 0; i < analysis.Summaries.Count; i++)
            {
                var summary = analysis.Summaries[i];
                var row = new Rectangle(0, y, width, rowHeight - Px(Theme.Space1));
                rowBounds.Add(row);
                if (g != null)
                {
                    DrawRow(g, summary, row, summary.Variable == selectedVariable, i == hovered);
                }
                y += rowHeight;
            }
            y += Px(Theme.Space2);

            var chosen = Selected;
            if (chosen != null && chart.Visible)
            {
                string caption = "Monthly anomaly over the zone, " + chosen.Units;
                if (g != null)
                {
                    Draw.Caption(g, caption, new Rectangle(0, y, width, Px(16)), Theme.TextSecondary);
                }
                y += Px(16 + Theme.Space1);
                var chartBounds = new Rectangle(0, y, width, Px(170));
                if (chart.Bounds != chartBounds)
                {
                    chart.Bounds = chartBounds;
                }
                y += chartBounds.Height + Px(Theme.Space1);
                y = Paragraph(g, "The dashed line is a guide for the eye. The figures above are the analysis service's.", y, width);
            }

            if (chosen != null)
            {
                if (chosen.Preprocessing?.Aggregation is string aggregation && aggregation.Length > 0)
                {
                    y = Paragraph(g, aggregation, y, width);
                }
                foreach (string limitation in chosen.Limitations)
                {
                    y = Paragraph(g, limitation, y, width);
                }
            }
            return y + Px(Theme.Space2);
        }

        private int Paragraph(Graphics? g, string text, int y, int width, Color? colour = null)
        {
            int height = TextHeight(text, Theme.Caption, width);
            if (g != null)
            {
                Draw.Text(g, text, Theme.Caption, new Rectangle(0, y, width, height), colour ?? Theme.TextMuted, Wrapped);
            }
            return y + height + Px(Theme.Space2);
        }

        private void DrawRow(Graphics g, ZoneSummary summary, Rectangle row, bool selected, bool hover)
        {
            if (selected || hover)
            {
                Draw.RoundedBox(g, row, Px(Theme.RadiusSmall), selected ? Theme.SurfaceHover : Theme.SurfaceRaised);
            }
            if (selected)
            {
                using var bar = new SolidBrush(Theme.Accent);
                g.FillRectangle(bar, row.X, row.Y + Px(10), Px(2), row.Height - Px(20));
            }

            int pad = Px(Theme.Space3);
            int half = row.Height / 2;
            var upper = new Rectangle(row.X + pad, row.Y + Px(4), row.Width - pad * 2, half - Px(2));
            var lower = new Rectangle(row.X + pad, row.Y + half, row.Width - pad * 2, half - Px(4));

            // TextSecondary is the app's colour for "not significant". Rising or falling takes no colour: neither is good or bad.
            var (trend, detail, interval) = RowText(summary);
            // A result the service cautions about is not given the weight of a significant one, whatever its p-value.
            bool cautioned = !string.IsNullOrEmpty(summary.Caution);
            var weight = summary.Tested && summary.Significant && !cautioned ? Theme.TextPrimary : Theme.TextSecondary;
            Draw.Text(g, FindingsList.LabelText(summary.Variable), selected ? Theme.BodyStrong : Theme.Body, upper, selected ? Theme.TextPrimary : weight);
            Draw.Text(g, trend, Theme.Numeric, upper, summary.Tested ? weight : Theme.TextMuted, RightAligned);
            Draw.Text(g, detail, Theme.NumericSmall, lower, cautioned ? Theme.Warning : Theme.TextMuted);
            Draw.Text(g, interval, Theme.NumericSmall, lower, Theme.TextMuted, RightAligned);
        }

        /// <summary>The three pieces of a row: the trend, the line under the name (the p-value, or why there is none), and the interval.</summary>
        private static (string Trend, string Detail, string Interval) RowText(ZoneSummary summary)
        {
            if (!summary.Tested)
            {
                return ("Not tested", summary.SkipReason == "no_cells" ? "None of the zone's cells were scanned for this" : "Too little data over the zone to test", "");
            }
            if (summary.Result is not TrendResult result)
            {
                return ("No trend to report", "Flat, or a median slope of zero", "");
            }

            // The heading above the rows says "per decade", so the unit here is the variable's own.
            const string perDecade = " per decade";
            string units = result.Units.EndsWith(perDecade, StringComparison.Ordinal) ? result.Units[..^perDecade.Length] : result.Units;
            string trend = Signed(result.SlopePerDecade) + " " + units;
            string p = result.PValue < 0.001 ? "p < 0.001" : "p = " + result.PValue.ToString(result.PValue < 0.01 ? "0.000" : "0.00", British);
            if (!string.IsNullOrEmpty(summary.Caution))
            {
                // The caution takes the interval's place on the line: the number is not one to dwell on.
                return (trend, summary.Caution + ", " + p, "");
            }
            return (trend, summary.Significant ? p : p + ", not significant", Signed(result.CiLow) + " to " + Signed(result.CiHigh));
        }

        /// <summary>The level of the intervals shown, such as "95%", from the first variable that has one.</summary>
        private static string IntervalLevel(ZoneAnalysis result)
        {
            double level = result.Summaries.FirstOrDefault(summary => summary.Result != null)?.Result?.CiLevel ?? 0.95;
            return (level * 100).ToString("0", British) + "%";
        }

        /// <summary>A number to three significant figures with its sign, and a true minus sign.</summary>
        private static string Signed(double value)
        {
            if (value == 0 || double.IsNaN(value))
            {
                return "0";
            }
            int decimals = Math.Clamp(2 - (int)Math.Floor(Math.Log10(Math.Abs(value))), 0, 6);
            string text = Math.Abs(value).ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), British);
            return (value < 0 ? "−" : "+") + text;
        }

        private static string ExtentText(ZoneAnalysis result)
        {
            string cells = result.Zone.CellCount == 1 ? "1 grid cell" : result.Zone.CellCount.ToString("N0", British) + " grid cells";
            double area = result.Zone.AreaKm2;
            string size = area >= 1e6
                ? (area / 1e6).ToString("0.00", British) + " million km²"
                : area.ToString("N0", British) + " km²";
            return cells + ", " + size + (result.Synthetic ? ", made-up data" : "");
        }

        private int RowAt(Point location)
        {
            for (int i = 0; i < rowBounds.Count; i++)
            {
                if (rowBounds[i].Contains(location))
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
            if (e.Button == MouseButtons.Left && analysis != null && hit >= 0 && hit < analysis.Summaries.Count)
            {
                VariableClicked?.Invoke(this, analysis.Summaries[hit].Variable);
            }
        }
    }
}
