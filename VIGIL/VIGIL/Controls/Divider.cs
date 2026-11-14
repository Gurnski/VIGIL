using System.ComponentModel;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  Hairline separator. Horizontal ones can carry an uppercase label at the left end,
    ///  which makes them section headings inside a card.
    /// </summary>
    public class Divider : VigilControl
    {
        private string label = "";
        private Orientation orientation = Orientation.Horizontal;

        protected override Size DefaultSize => new(200, 16);

        [DefaultValue("")]
        public string Label
        {
            get => label;
            set { label = value ?? ""; Invalidate(); }
        }

        [DefaultValue(Orientation.Horizontal)]
        public Orientation Orientation
        {
            get => orientation;
            set { orientation = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            if (orientation == Orientation.Vertical)
            {
                Draw.VerticalLine(g, Width / 2, 0, Height - 1, Theme.Divider);
                return;
            }

            int x = 0;
            if (label.Length > 0)
            {
                var size = Draw.Measure(g, label.ToUpperInvariant(), Theme.CaptionStrong);
                Draw.Caption(g, label, new Rectangle(0, 0, size.Width + 1, Height), Theme.TextMuted);
                x = size.Width + Px(Theme.Space3);
            }
            if (x < Width)
            {
                Draw.HorizontalLine(g, x, Width - 1, Height / 2, Theme.Divider);
            }
        }
    }
}
