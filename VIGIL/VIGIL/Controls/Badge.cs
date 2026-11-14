using System.ComponentModel;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    public enum BadgeTone
    {
        Neutral,
        Accent,
        Positive,
        Negative,
        Warning
    }

    /// <summary>
    ///  Small outlined label, such as "Significant, p < 0.05" or a dataset name. Coloured tones
    ///  tint the fill and colour the text; Neutral is a quiet raised surface. Resizes itself to fit.
    /// </summary>
    public class Badge : VigilControl
    {
        private BadgeTone tone = BadgeTone.Neutral;
        private bool showDot;

        public Badge()
        {
            Font = Theme.CaptionStrong;
            FitToText();
        }

        [DefaultValue(BadgeTone.Neutral)]
        public BadgeTone Tone
        {
            get => tone;
            set { tone = value; Invalidate(); }
        }

        /// <summary>
        ///  Adds a status dot before the text, for lists where the colour alone is easy to miss.
        /// </summary>
        [DefaultValue(false)]
        public bool ShowDot
        {
            get => showDot;
            set { showDot = value; FitToText(); Invalidate(); }
        }

        private Color ToneColour => tone switch
        {
            BadgeTone.Accent => Theme.Accent,
            BadgeTone.Positive => Theme.Positive,
            BadgeTone.Negative => Theme.Negative,
            BadgeTone.Warning => Theme.Warning,
            _ => Theme.TextSecondary
        };

        private int PadX => Px(Theme.Space2 + 2);
        private int DotWidth => showDot ? Px(Theme.Space3) : 0;

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); FitToText(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitToText(); }

        private void FitToText()
        {
            var text = TextRenderer.MeasureText(Text.Length > 0 ? Text : " ", Font, Size.Empty, TextFormatFlags.NoPadding);
            Size = new Size(text.Width + PadX * 2 + DotWidth, Math.Max(text.Height + Px(Theme.Space2), Px(22)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            bool neutral = tone == BadgeTone.Neutral;
            var colour = neutral ? Theme.TextSecondary : ToneColour;
            if (Text.Length == 0)
            {
                return;
            }

            // An outline in a quiet mix of the tone: the text carries the colour. The fill is the
            // card surface, so it disappears on a card and keeps the text readable over the globe.
            var edge = neutral ? Theme.Border : Draw.Blend(Theme.Surface, colour, 0.45f);
            Draw.RoundedBox(g, OutlineBounds, Px(Theme.RadiusSmall), Theme.Surface, edge);

            int x = PadX;
            if (showDot)
            {
                Draw.Circle(g, new PointF(x + Px(2.5f), Height / 2f), Px(2.5f), colour);
                x += DotWidth;
            }
            Draw.Text(g, Text, Font, new Rectangle(x, 0, Width - x - PadX + 1, Height), colour);
        }
    }
}
