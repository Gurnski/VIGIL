using System.ComponentModel;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    public enum TrendArrow
    {
        None,
        Up,
        Down
    }

    /// <summary>
    ///  Headline number tile: a caption, a big value, a coloured change line and an optional sparkline.
    /// </summary>
    public class StatTile : VigilControl
    {
        private const TextFormatFlags TopLeft =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private string caption = "Caption";
        private string value = "0";
        private string delta = "";
        private TrendArrow arrow = TrendArrow.None;
        private Color deltaColour = Theme.Accent;
        private double[] sparkline = Array.Empty<double>();

        protected override Size DefaultSize => new(280, 132);

        [DefaultValue("Caption")]
        public string Caption
        {
            get => caption;
            set { caption = value ?? ""; Invalidate(); }
        }

        [DefaultValue("0")]
        public string Value
        {
            get => value;
            set { this.value = value ?? ""; Invalidate(); }
        }

        /// <summary>
        ///  Short change text shown under the value, such as "per decade · p < 0.05".
        /// </summary>
        [DefaultValue("")]
        public string Delta
        {
            get => delta;
            set { delta = value ?? ""; Invalidate(); }
        }

        [DefaultValue(TrendArrow.None)]
        public TrendArrow Arrow
        {
            get => arrow;
            set { arrow = value; Invalidate(); }
        }

        /// <summary>
        ///  Colour of the change line and sparkline. Pick by meaning, not direction (see <see cref="Theme"/>).
        /// </summary>
        public Color DeltaColour
        {
            get => deltaColour;
            set { deltaColour = value; Invalidate(); }
        }

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double[] Sparkline
        {
            get => sparkline;
            set { sparkline = value ?? Array.Empty<double>(); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);
            Draw.RoundedBox(g, new Rectangle(0, 0, Width, Height), Px(Theme.Radius), Theme.Surface);

            int pad = Px(Theme.CardPadding);
            Draw.Caption(g, caption, new Rectangle(pad, pad, Width - pad * 2, Px(16)), Theme.TextMuted);

            int valueTop = pad + Px(Theme.Space5);
            var valueSize = Draw.Measure(g, value, Theme.Display);
            Draw.Text(g, value, Theme.Display, new Rectangle(pad - Px(1), valueTop, valueSize.Width + Px(2), valueSize.Height), Theme.TextPrimary, TopLeft);

            if (sparkline.Length > 1)
            {
                float left = Math.Max(Width * 0.6f, pad + valueSize.Width + Px(Theme.Space4));
                var area = new RectangleF(left, valueTop + Px(Theme.Space2), Width - pad - left, Height - pad - valueTop - Px(Theme.Space2));
                if (area.Width > Px(20) && area.Height > Px(10))
                {
                    Draw.AreaLine(g, Draw.Fit(sparkline, area), area.Bottom, Draw.WithAlpha(deltaColour, 200), Px(1.5f), 26);
                }
            }

            if (delta.Length > 0)
            {
                int top = valueTop + valueSize.Height + Px(Theme.Space1);
                int x = pad;
                string arrowGlyph = arrow switch
                {
                    TrendArrow.Up => "▲",
                    TrendArrow.Down => "▼",
                    _ => ""
                };
                if (arrowGlyph.Length > 0)
                {
                    var arrowSize = Draw.Measure(g, arrowGlyph, Theme.Caption);
                    Draw.Text(g, arrowGlyph, Theme.Caption, new Rectangle(x, top, arrowSize.Width + 1, Px(16)), deltaColour);
                    x += arrowSize.Width + Px(Theme.Space1);
                }
                Draw.Text(g, delta, Theme.CaptionStrong, new Rectangle(x, top, Width - pad - x, Px(16)), deltaColour);
            }
        }
    }
}
