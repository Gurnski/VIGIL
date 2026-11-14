using System.ComponentModel;
using System.Globalization;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  Line chart for one time series: a soft area fill, a least-squares guide line, a hover
    ///  read-out, and a left-to-right reveal whenever new data arrives.
    ///  The guide line is only a visual aid. Real trend and significance tests belong in Analysis/.
    /// </summary>
    public class TimeSeriesChart : VigilControl
    {
        private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

        private const TextFormatFlags AxisRight =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        private const TextFormatFlags AxisTop =
            TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        private readonly Transition reveal;
        private DateTime[] dates = Array.Empty<DateTime>();
        private double[] values = Array.Empty<double>();
        private PointF[] points = Array.Empty<PointF>();
        private int hoverIndex = -1;
        private bool showTrendLine = true;
        private string units = "";
        private string valueFormat = "0.00";

        public TimeSeriesChart()
        {
            BackColor = Theme.Surface;
            reveal = new Transition(Invalidate) { Speed = 0.1f };
            reveal.Jump(1f);
        }

        protected override Size DefaultSize => new(600, 300);

        [DefaultValue(true)]
        public bool ShowTrendLine
        {
            get => showTrendLine;
            set { showTrendLine = value; Invalidate(); }
        }

        /// <summary>
        ///  Shown after values in the hover read-out, for example "°C" or "mm/day".
        /// </summary>
        [DefaultValue("")]
        public string Units
        {
            get => units;
            set { units = value ?? ""; Invalidate(); }
        }

        [DefaultValue("0.00")]
        public string ValueFormat
        {
            get => valueFormat;
            set { valueFormat = value ?? "0.00"; Invalidate(); }
        }

        /// <summary>
        ///  Replaces the series and replays the reveal animation. Dates must be in ascending order.
        /// </summary>
        public void SetData(IReadOnlyList<DateTime> dates, IReadOnlyList<double> values)
        {
            if (dates.Count != values.Count)
            {
                throw new ArgumentException("dates and values must be the same length.");
            }

            this.dates = dates.ToArray();
            this.values = values.ToArray();
            hoverIndex = -1;
            reveal.Jump(0f);
            reveal.To(1f);
        }

        // Left gutter holds the value labels; the top row holds the legend; the bottom row holds the dates.
        private Rectangle PlotArea => Rectangle.FromLTRB(
            Px(Theme.Space10 + Theme.Space2),
            Px(Theme.Space6 + Theme.Space1),
            Width - Px(Theme.Space2),
            Height - Px(Theme.Space6 + Theme.Space1));

        /// <summary>
        ///  Formats a value with a true minus sign rather than a hyphen.
        /// </summary>
        private string FormatValue(double value) => value.ToString(valueFormat, British).Replace('-', '−');

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            var plot = PlotArea;
            if (values.Length == 0 || plot.Width <= 0 || plot.Height <= 0)
            {
                points = Array.Empty<PointF>();
                Draw.Text(g, "No data", Theme.Body, ClientRectangle, Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var (low, high, step) = NiceScale(values.Min(), values.Max());
            DrawValueAxis(g, plot, low, high, step);
            DrawTimeAxis(g, plot);
            points = Project(plot, low, high);

            int revealed = (int)Math.Ceiling(plot.Width * reveal.Value);
            g.SetClip(new Rectangle(plot.Left - Px(4), 0, revealed + Px(8), Height));
            Draw.AreaLine(g, points, plot.Bottom, Theme.Series, Px(1.5f), 0);
            if (showTrendLine && values.Length > 1)
            {
                DrawTrendLine(g, plot, low, high);
            }
            g.ResetClip();

            DrawLegend(g);
            if (hoverIndex >= 0 && hoverIndex < points.Length && !reveal.IsRunning)
            {
                DrawHover(g, plot);
            }
        }

        private float ToY(Rectangle plot, double value, double low, double high)
        {
            return plot.Bottom - (float)((value - low) / (high - low)) * plot.Height;
        }

        private PointF[] Project(Rectangle plot, double low, double high)
        {
            double start = dates[0].Ticks;
            double span = dates[^1].Ticks - start;
            var result = new PointF[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                float x = span > 0
                    ? plot.Left + (float)((dates[i].Ticks - start) / span) * plot.Width
                    : plot.Left + plot.Width / 2f;
                result[i] = new PointF(x, ToY(plot, values[i], low, high));
            }
            return result;
        }

        private void DrawValueAxis(Graphics g, Rectangle plot, double low, double high, double step)
        {
            int lines = (int)Math.Round((high - low) / step);
            for (int i = 0; i <= lines; i++)
            {
                double value = low + i * step;
                int y = (int)Math.Round(ToY(plot, value, low, high));
                bool isZero = Math.Abs(value) < step / 1000;
                Draw.HorizontalLine(g, plot.Left, plot.Right, y, isZero ? Theme.Border : Theme.GridLine);

                var label = new Rectangle(0, y - Px(8), plot.Left - Px(Theme.Space3), Px(16));
                Draw.Text(g, FormatValue(value), Theme.NumericSmall, label, Theme.TextMuted, AxisRight);
            }
        }

        private void DrawTimeAxis(Graphics g, Rectangle plot)
        {
            var span = dates[^1] - dates[0];
            int labelWidth = Px(100);
            int labelTop = plot.Bottom + Px(Theme.Space2 + 2);

            if (span.TotalDays > 3 * 365)
            {
                // Label 1 January on a round year interval that fits the width.
                int firstYear = dates[0].Year;
                int lastYear = dates[^1].Year;
                int maxLabels = Math.Max(2, plot.Width / Px(80));
                int step = new[] { 1, 2, 5, 10, 20, 50, 100 }.FirstOrDefault(s => (lastYear - firstYear) / s + 1 <= maxLabels, 100);

                for (int year = (firstYear + step - 1) / step * step; year <= lastYear; year += step)
                {
                    var date = new DateTime(year, 1, 1);
                    if (date < dates[0] || date > dates[^1])
                    {
                        continue;
                    }

                    int x = plot.Left + (int)(plot.Width * ((date - dates[0]).Ticks / (double)span.Ticks));
                    Draw.VerticalLine(g, x, plot.Bottom + 1, plot.Bottom + Px(Theme.Space1), Theme.GridLine);
                    int left = Math.Clamp(x - labelWidth / 2, 0, Width - labelWidth);
                    Draw.Text(g, year.ToString(British), Theme.NumericSmall,
                        new Rectangle(left, labelTop, labelWidth, Px(16)), Theme.TextMuted,
                        AxisTop | TextFormatFlags.HorizontalCenter);
                }
                return;
            }

            int count = Math.Max(2, plot.Width / Px(110));
            for (int i = 0; i < count; i++)
            {
                float fraction = i / (float)(count - 1);
                var date = dates[0] + TimeSpan.FromTicks((long)(span.Ticks * (double)fraction));
                int x = plot.Left + (int)(plot.Width * fraction);

                TextFormatFlags align;
                int left;
                if (i == 0) { align = TextFormatFlags.Left; left = x; }
                else if (i == count - 1) { align = TextFormatFlags.Right; left = x - labelWidth; }
                else { align = TextFormatFlags.HorizontalCenter; left = x - labelWidth / 2; }

                Draw.Text(g, date.ToString("MMM yyyy", British), Theme.NumericSmall,
                    new Rectangle(left, labelTop, labelWidth, Px(16)), Theme.TextMuted, AxisTop | align);
            }
        }

        private void DrawTrendLine(Graphics g, Rectangle plot, double low, double high)
        {
            // Ordinary least squares on time in years since the first point.
            int n = values.Length;
            double[] years = dates.Select(d => (d - dates[0]).TotalDays / 365.25).ToArray();
            double meanX = years.Average();
            double meanY = values.Average();
            double covariance = 0, variance = 0;
            for (int i = 0; i < n; i++)
            {
                covariance += (years[i] - meanX) * (values[i] - meanY);
                variance += (years[i] - meanX) * (years[i] - meanX);
            }
            double slope = variance > 0 ? covariance / variance : 0;
            double intercept = meanY - slope * meanX;

            using var pen = new Pen(Theme.Warning, Px(1.5f)) { DashPattern = new[] { 3f, 2.5f } };
            g.DrawLine(pen,
                points[0].X, ToY(plot, intercept, low, high),
                points[^1].X, ToY(plot, intercept + slope * years[^1], low, high));
        }

        private void DrawLegend(Graphics g)
        {
            var entries = new List<(string Label, Color Colour, bool Dashed)> { ("Data", Theme.Series, false) };
            if (showTrendLine)
            {
                entries.Add(("Linear trend", Theme.Warning, true));
            }

            int x = Width - Px(Theme.Space2);
            int rowHeight = Px(16);
            int sample = Px(14);
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var (label, colour, dashed) = entries[i];
                var size = Draw.Measure(g, label, Theme.Caption);
                x -= size.Width;
                Draw.Text(g, label, Theme.Caption, new Rectangle(x, 0, size.Width + 1, rowHeight), Theme.TextSecondary);
                x -= Px(Theme.Space2) + sample;

                using var pen = new Pen(colour, Px(1.5f));
                if (dashed)
                {
                    pen.DashPattern = new[] { 3f, 2.5f };
                }
                g.DrawLine(pen, x, rowHeight / 2f, x + sample, rowHeight / 2f);
                x -= Px(Theme.Space4);
            }
        }

        private void DrawHover(Graphics g, Rectangle plot)
        {
            var point = points[hoverIndex];
            int x = (int)Math.Round(point.X);
            Draw.VerticalLine(g, x, plot.Top, plot.Bottom, Draw.WithAlpha(Theme.TextSecondary, 64));
            Draw.Marker(g, point, Px(3.5f), Theme.Series, Theme.Surface, Px(2f));

            string when = dates[hoverIndex].ToString("MMMM yyyy", British);
            string reading = FormatValue(values[hoverIndex]) + (units.Length > 0 ? " " + units : "");
            var whenSize = Draw.Measure(g, when, Theme.Caption);
            var readingSize = Draw.Measure(g, reading, Theme.NumericStrong);

            int padX = Px(Theme.Space3);
            int padY = Px(Theme.Space2);
            int width = Math.Max(whenSize.Width, readingSize.Width) + padX * 2;
            int height = padY * 2 + whenSize.Height + Px(2) + readingSize.Height;
            int left = x + Px(Theme.Space4);
            if (left + width > plot.Right)
            {
                left = x - Px(Theme.Space4) - width;
            }
            int top = (int)Math.Clamp(point.Y - height / 2f, plot.Top, Math.Max(plot.Top, plot.Bottom - height));

            var box = new Rectangle(left, top, width, height);
            Draw.RoundedBox(g, box, Px(Theme.RadiusSmall), Theme.SurfaceRaised);
            Draw.Text(g, when, Theme.Caption,
                new Rectangle(box.X + padX, box.Y + padY, whenSize.Width + 1, whenSize.Height), Theme.TextSecondary);
            Draw.Text(g, reading, Theme.NumericStrong,
                new Rectangle(box.X + padX, box.Y + padY + whenSize.Height + Px(2), readingSize.Width + 1, readingSize.Height), Theme.TextPrimary);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (points.Length == 0)
            {
                return;
            }

            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                float distance = Math.Abs(points[i].X - e.X);
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }

            if (nearest != hoverIndex)
            {
                hoverIndex = nearest;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverIndex = -1;
            Invalidate();
        }

        /// <summary>
        ///  Rounds the axis range out to tidy numbers (1, 2 or 5 times a power of ten).
        /// </summary>
        private static (double Low, double High, double Step) NiceScale(double min, double max)
        {
            if (max - min < 1e-9)
            {
                min -= 1;
                max += 1;
            }

            double step = NiceNumber(NiceNumber(max - min, false) / 4, true);
            return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
        }

        private static double NiceNumber(double range, bool round)
        {
            double exponent = Math.Floor(Math.Log10(range));
            double fraction = range / Math.Pow(10, exponent);
            double nice = round
                ? (fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10)
                : (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10);
            return nice * Math.Pow(10, exponent);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reveal.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
