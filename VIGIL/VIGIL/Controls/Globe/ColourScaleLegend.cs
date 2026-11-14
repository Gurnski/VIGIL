using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using VIGIL.Theming;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  Small legend for a <see cref="ColourScale"/>: a title, the gradient bar and
    ///  the values at each end. Place it over the globe next to a grid overlay.
    /// </summary>
    public class ColourScaleLegend : VigilControl
    {
        private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

        private ColourScale scale = ColourScale.BlueWhiteRed;
        private double minimum = -1;
        private double maximum = 1;
        private string title = "Legend";
        private string units = "";
        private string valueFormat = "0.0";

        protected override Size DefaultSize => Theme.Scaled(240, 68);

        [Browsable(false)]
        public ColourScale ColourScale
        {
            get => scale;
            set { scale = value ?? ColourScale.BlueWhiteRed; Invalidate(); }
        }

        [DefaultValue(-1.0)]
        public double Minimum
        {
            get => minimum;
            set { minimum = value; Invalidate(); }
        }

        [DefaultValue(1.0)]
        public double Maximum
        {
            get => maximum;
            set { maximum = value; Invalidate(); }
        }

        [DefaultValue("Legend")]
        public string Title
        {
            get => title;
            set { title = value ?? ""; Invalidate(); }
        }

        /// <summary>Appended to the end labels, for example "°C/decade".</summary>
        [DefaultValue("")]
        public string Units
        {
            get => units;
            set { units = value ?? ""; Invalidate(); }
        }

        [DefaultValue("0.0")]
        public string ValueFormat
        {
            get => valueFormat;
            set { valueFormat = value ?? "0.0"; Invalidate(); }
        }

        /// <summary>
        ///  Keeps the legend in step with a globe's grid overlay: its scale and range are copied
        ///  whenever the overlay changes, and the legend hides when there is no overlay.
        /// </summary>
        public void Follow(GlobeControl globe)
        {
            globe.GridOverlayChanged += (s, e) => CopyFrom(globe);
            CopyFrom(globe);
        }

        private void CopyFrom(GlobeControl globe)
        {
            var overlayScale = globe.GridOverlayScale;
            Visible = overlayScale != null;
            if (overlayScale == null)
            {
                return;
            }
            scale = overlayScale;
            minimum = globe.GridOverlayMinimum ?? minimum;
            maximum = globe.GridOverlayMaximum ?? maximum;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);
            Draw.RoundedBox(g, OutlineBounds, Px(Theme.RadiusSmall), Theme.Surface, Theme.Border);

            int edge = Px(12);
            Draw.Text(g, title, Theme.CaptionStrong, new Rectangle(edge, Px(8), Width - edge * 2, Px(16)), Theme.TextSecondary);

            var bar = new Rectangle(edge, Px(30), Width - edge * 2, Px(10));
            if (bar.Width > 1)
            {
                const int stops = 33;
                var blend = new ColorBlend(stops);
                for (int i = 0; i < stops; i++)
                {
                    float t = i / (float)(stops - 1);
                    blend.Positions[i] = t;
                    blend.Colors[i] = Draw.WithAlpha(scale.At(t), (int)(255 * scale.OpacityAt(t)));
                }

                using var brush = new LinearGradientBrush(new Rectangle(bar.X - 1, bar.Y, bar.Width + 2, bar.Height), Color.Black, Color.White, LinearGradientMode.Horizontal)
                {
                    InterpolationColors = blend
                };
                using var path = Draw.RoundedRect(bar, Px(3f));
                g.FillPath(brush, path);
            }

            int labelTop = bar.Bottom + Px(4);
            string suffix = units.Length > 0 ? " " + units : "";
            string low = minimum.ToString(valueFormat, British) + suffix;
            string high = maximum.ToString(valueFormat, British) + suffix;
            Draw.Text(g, low, Theme.Caption, new Rectangle(edge, labelTop, Width / 2 - edge, Px(16)), Theme.TextMuted);
            Draw.Text(g, high, Theme.Caption, new Rectangle(Width / 2, labelTop, Width / 2 - edge, Px(16)), Theme.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            if (scale.Diverging)
            {
                double middle = (minimum + maximum) / 2;
                Draw.Text(g, middle.ToString(valueFormat, British), Theme.Caption, new Rectangle(Width / 4, labelTop, Width / 2, Px(16)), Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
