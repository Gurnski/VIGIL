using System.Drawing.Drawing2D;

namespace VIGIL.Theming
{
    /// <summary>
    ///  Drawing helpers shared by the custom controls.
    ///
    ///  Pixel convention: after <see cref="Smooth"/>, pixel edges sit on whole numbers. Fill shapes
    ///  at whole-number coordinates and put 1px strokes on the half pixel (x + 0.5), as
    ///  <c>VigilControl.OutlineBounds</c> does, so lines land on one row of pixels instead of two.
    /// </summary>
    public static class Draw
    {
        public const TextFormatFlags DefaultText =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.None;
        }

        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0f)
            {
                path.AddRectangle(bounds);
                return path;
            }

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        ///  Fills and optionally outlines a rounded rectangle in one call.
        /// </summary>
        public static void RoundedBox(Graphics g, RectangleF bounds, float radius, Color fill, Color? border = null)
        {
            using var path = RoundedRect(bounds, radius);
            if (fill.A > 0)
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            if (border is Color colour)
            {
                using var pen = new Pen(colour);
                g.DrawPath(pen, path);
            }
        }

        /// <summary>
        ///  Crisp 1px horizontal line covering pixel row y from x1 to x2 inclusive.
        /// </summary>
        public static void HorizontalLine(Graphics g, int x1, int x2, int y, Color colour)
        {
            using var pen = new Pen(colour);
            g.DrawLine(pen, x1, y + 0.5f, x2 + 1, y + 0.5f);
        }

        /// <summary>
        ///  Crisp 1px vertical line covering pixel column x from y1 to y2 inclusive.
        /// </summary>
        public static void VerticalLine(Graphics g, int x, int y1, int y2, Color colour)
        {
            using var pen = new Pen(colour);
            g.DrawLine(pen, x + 0.5f, y1, x + 0.5f, y2 + 1);
        }

        public static void Circle(Graphics g, PointF centre, float radius, Color colour)
        {
            using var brush = new SolidBrush(colour);
            g.FillEllipse(brush, centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f);
        }

        /// <summary>
        ///  A filled dot with a contrasting ring, for chart markers.
        /// </summary>
        public static void Marker(Graphics g, PointF centre, float radius, Color fill, Color ring, float ringWidth)
        {
            Circle(g, centre, radius + ringWidth, ring);
            Circle(g, centre, radius, fill);
        }

        public static void Text(Graphics g, string text, Font font, Rectangle bounds, Color colour, TextFormatFlags flags = DefaultText)
        {
            TextRenderer.DrawText(g, text, font, bounds, colour, flags);
        }

        /// <summary>
        ///  Uppercase label in the caption weight, for card titles and tile captions.
        /// </summary>
        public static void Caption(Graphics g, string text, Rectangle bounds, Color colour, TextFormatFlags flags = DefaultText)
        {
            TextRenderer.DrawText(g, text.ToUpperInvariant(), Theme.CaptionStrong, bounds, colour, flags);
        }

        public static Size Measure(Graphics g, string text, Font font)
        {
            return TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        public static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            int Lerp(int a, int b) => (int)Math.Round(a + (b - a) * amount);
            return Color.FromArgb(Lerp(from.A, to.A), Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
        }

        public static Color WithAlpha(Color colour, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), colour);

        /// <summary>
        ///  Maps values onto an area left to right, scaled to fill its height. Used for sparklines.
        /// </summary>
        public static PointF[] Fit(IReadOnlyList<double> values, RectangleF area)
        {
            if (values.Count == 0)
            {
                return Array.Empty<PointF>();
            }

            double min = values.Min();
            double range = values.Max() - min;
            if (range <= 0)
            {
                range = 1;
            }

            var points = new PointF[values.Count];
            float stepX = values.Count > 1 ? area.Width / (values.Count - 1) : 0f;
            for (int i = 0; i < values.Count; i++)
            {
                float y = area.Bottom - (float)((values[i] - min) / range) * area.Height;
                points[i] = new PointF(area.Left + i * stepX, y);
            }
            return points;
        }

        /// <summary>
        ///  Draws a line with a gradient fill fading down to the baseline.
        /// </summary>
        public static void AreaLine(Graphics g, PointF[] points, float baseline, Color colour, float lineWidth, int fillAlpha)
        {
            if (points.Length < 2)
            {
                return;
            }

            if (fillAlpha > 0)
            {
                using var area = new GraphicsPath();
                area.AddLines(points);
                area.AddLine(points[^1].X, points[^1].Y, points[^1].X, baseline);
                area.AddLine(points[^1].X, baseline, points[0].X, baseline);
                area.CloseFigure();

                float top = points.Min(p => p.Y);
                var gradientBounds = new RectangleF(points[0].X, top - 1f,
                    Math.Max(1f, points[^1].X - points[0].X), Math.Max(1f, baseline - top + 2f));
                using var fill = new LinearGradientBrush(gradientBounds, WithAlpha(colour, fillAlpha), WithAlpha(colour, 0), LinearGradientMode.Vertical);
                fill.WrapMode = WrapMode.TileFlipXY;
                g.FillPath(fill, area);
            }

            using var pen = new Pen(colour, lineWidth)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLines(pen, points);
        }
    }
}
