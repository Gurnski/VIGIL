using VIGIL.Theming;

namespace VIGIL.Controls.Chat
{
    /// <summary>
    ///  A one-line note above the chat input saying what the next question is about, such as
    ///  a finding picked on the map. The cross on its right removes it.
    /// </summary>
    public class ContextChip : VigilControl
    {
        private bool crossHovered;

        /// <summary>Raised when the cross is clicked.</summary>
        public event EventHandler? Removed;

        protected override Size DefaultSize => Theme.Scaled(320, 30);

        private Rectangle CrossBounds => new(Width - Height, 0, Height, Height);

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);
            Draw.RoundedBox(g, OutlineBounds, Px(Theme.RadiusSmall), Theme.Surface, Theme.Border);

            int pad = Px(Theme.Space3);
            var label = Draw.Measure(g, "Asking about", Theme.CaptionStrong);
            Draw.Text(g, "Asking about", Theme.CaptionStrong, new Rectangle(pad, 0, label.Width + 1, Height), Theme.TextSecondary);
            int textX = pad + label.Width + Px(Theme.Space2);
            Draw.Text(g, Text, Theme.Caption, new Rectangle(textX, 0, CrossBounds.Left - textX, Height), Theme.TextPrimary);
            Draw.Text(g, Icons.Cancel, Theme.Icon(8f), CrossBounds, crossHovered ? Theme.TextPrimary : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = CrossBounds.Contains(e.Location);
            if (over != crossHovered)
            {
                crossHovered = over;
                Cursor = over ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            crossHovered = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && CrossBounds.Contains(e.Location))
            {
                Removed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
