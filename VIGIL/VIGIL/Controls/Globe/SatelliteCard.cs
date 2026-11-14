using System.Drawing.Drawing2D;
using VIGIL.Theming;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  A small panel about one satellite, shown over the globe when it is clicked: its
    ///  picture, its name, whether its data is in the scan, a few facts and a note. It sets its
    ///  own height to fit. The cross in the corner raises <see cref="CloseRequested"/>.
    /// </summary>
    public class SatelliteCard : VigilControl
    {
        private const TextFormatFlags Wrapped =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl
            | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private string title = "";
        private string status = "";
        private Color statusColour = Theme.TextSecondary;
        private Image? picture;
        private IReadOnlyList<(string Term, string Value)> facts = Array.Empty<(string, string)>();
        private string note = "";
        private bool crossHovered;

        /// <summary>Raised when the cross is clicked.</summary>
        public event EventHandler? CloseRequested;

        protected override Size DefaultSize => Theme.Scaled(300, 320);

        /// <summary>Fills the card. The picture is not owned by the card and is not disposed with it.</summary>
        public void SetContent(string title, string status, Color statusColour, Image? picture,
            IReadOnlyList<(string Term, string Value)> facts, string note)
        {
            this.title = title;
            this.status = status;
            this.statusColour = statusColour;
            this.picture = picture;
            this.facts = facts;
            this.note = note;
            Height = Arrange(null);
            Invalidate();
        }

        private int Pad => Px(Theme.Space4);
        private int TermWidth => Px(76);
        private int PictureHeight => picture == null ? 0 : Px(88);
        private Rectangle CrossBounds => new(Width - Px(32), 0, Px(32), Px(32));

        private static int TextHeight(string text, Font font, int width) =>
            TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), Wrapped).Height;

        /// <summary>Lays the card out from the top, drawing as it goes when given a surface, and returns the height it needs.</summary>
        private int Arrange(Graphics? g)
        {
            int inner = Width - Pad * 2;
            int y = Pad;

            if (picture != null)
            {
                if (g != null)
                {
                    // Fitted inside its box and centred, keeping its shape.
                    float scale = Math.Min((float)inner / picture.Width, (float)PictureHeight / picture.Height);
                    float width = picture.Width * scale, height = picture.Height * scale;
                    var old = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(picture, Pad + (inner - width) / 2f, y + (PictureHeight - height) / 2f, width, height);
                    g.InterpolationMode = old;
                }
                y += PictureHeight + Px(Theme.Space3);
            }

            int titleHeight = Theme.BodyStrong.Height;
            if (g != null)
            {
                Draw.Text(g, title, Theme.BodyStrong, new Rectangle(Pad, y, inner, titleHeight), Theme.TextPrimary);
            }
            y += titleHeight + Px(2);

            int statusHeight = TextHeight(status, Theme.Caption, inner);
            if (g != null)
            {
                Draw.Text(g, status, Theme.Caption, new Rectangle(Pad, y, inner, statusHeight), statusColour, Wrapped);
            }
            y += statusHeight + Px(Theme.Space3);

            int valueLeft = Pad + TermWidth + Px(Theme.Space2);
            int valueWidth = Width - Pad - valueLeft;
            foreach (var (term, value) in facts)
            {
                int height = Math.Max(Theme.Caption.Height, TextHeight(value, Theme.Caption, valueWidth));
                if (g != null)
                {
                    Draw.Text(g, term, Theme.Caption, new Rectangle(Pad, y, TermWidth, Theme.Caption.Height), Theme.TextSecondary);
                    Draw.Text(g, value, Theme.Caption, new Rectangle(valueLeft, y, valueWidth, height), Theme.TextPrimary, Wrapped);
                }
                y += height + Px(Theme.Space1);
            }

            if (note.Length > 0)
            {
                y += Px(Theme.Space2);
                int noteHeight = TextHeight(note, Theme.Caption, inner);
                if (g != null)
                {
                    Draw.Text(g, note, Theme.Caption, new Rectangle(Pad, y, inner, noteHeight), Theme.TextMuted, Wrapped);
                }
                y += noteHeight;
            }
            return y + Pad;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);
            // A panel over the globe is one of the places an outline is allowed.
            Draw.RoundedBox(g, OutlineBounds, Px(Theme.Radius), Theme.Surface, Theme.Border);
            Arrange(g);
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
                Invalidate(CrossBounds);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (crossHovered)
            {
                crossHovered = false;
                Cursor = Cursors.Default;
                Invalidate(CrossBounds);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && CrossBounds.Contains(e.Location))
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
