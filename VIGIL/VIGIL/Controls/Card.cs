using System.ComponentModel;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  Rounded surface panel with an optional uppercase title. Drop other controls inside it;
    ///  the padding keeps them clear of the edges and the title. It has no outline: it is told
    ///  apart from the page by tone.
    /// </summary>
    public class Card : Panel
    {
        private string title = "";

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            ForeColor = Theme.TextPrimary;
            UpdatePadding();
        }

        [DefaultValue("")]
        public string Title
        {
            get => title;
            set { title = value ?? ""; UpdatePadding(); Invalidate(); }
        }

        /// <summary>
        ///  Where the title line sits, so a control placed on the same line (a badge, a toggle)
        ///  can be centred on it. Empty when there is no title.
        /// </summary>
        [Browsable(false)]
        public Rectangle TitleBounds => title.Length > 0
            ? new Rectangle(Px(Theme.CardPadding), Px(Theme.CardPadding), Width - Px(Theme.CardPadding) * 2, Px(16))
            : Rectangle.Empty;

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        private void UpdatePadding()
        {
            int edge = Px(Theme.CardPadding);
            Padding = new Padding(edge, title.Length > 0 ? Px(Theme.CardPadding + 16 + Theme.Space4) : edge, edge, edge);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);
            Draw.RoundedBox(g, new Rectangle(0, 0, Width, Height), Px(Theme.Radius), Theme.Surface);

            if (title.Length > 0)
            {
                Draw.Caption(g, title, TitleBounds, Theme.TextSecondary);
            }
        }
    }
}
