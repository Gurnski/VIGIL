using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Controls.Chat
{
    /// <summary>
    ///  The saved chats, one row each: the title and when it was last used. Click a row to
    ///  open it. The cross that appears on the row under the mouse deletes it. The control
    ///  is as tall as its rows, so put it in a scrolling panel.
    /// </summary>
    public class ConversationList : VigilControl
    {
        private IReadOnlyList<Conversation> items = Array.Empty<Conversation>();
        private string? selectedId;
        private int hovered = -1;
        private bool crossHovered;

        public event EventHandler<Conversation>? ConversationClicked;
        public event EventHandler<Conversation>? DeleteClicked;

        protected override Size DefaultSize => Theme.Scaled(240, 56);

        private int RowHeight => Px(52);

        public void SetItems(IReadOnlyList<Conversation> conversations, string? selected)
        {
            items = conversations;
            selectedId = selected;
            hovered = -1;
            Height = Math.Max(RowHeight, items.Count * RowHeight);
            Invalidate();
        }

        private Rectangle RowBounds(int index) => new(0, index * RowHeight, Width, RowHeight - Px(Theme.Space1));

        private Rectangle CrossBounds(int index)
        {
            var row = RowBounds(index);
            int size = Px(Theme.ControlHeightSmall);
            return new Rectangle(row.Right - size - Px(Theme.Space1), row.Y + (row.Height - size) / 2, size, size);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            if (items.Count == 0)
            {
                Draw.Text(g, "No saved chats yet.", Theme.Body, new Rectangle(Px(Theme.Space3), 0, Width - Px(Theme.Space3), RowHeight), Theme.TextMuted);
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                var row = RowBounds(i);
                bool selected = items[i].Id == selectedId;
                if (selected || i == hovered)
                {
                    Draw.RoundedBox(g, row, Px(Theme.RadiusSmall), selected ? Theme.SurfaceHover : Theme.SurfaceRaised);
                }
                if (selected)
                {
                    using var bar = new SolidBrush(Theme.Accent);
                    g.FillRectangle(bar, row.X, row.Y + Px(10), Px(2), row.Height - Px(20));
                }

                int pad = Px(Theme.Space3);
                int textWidth = (i == hovered ? CrossBounds(i).Left : row.Right) - pad * 2;
                int half = row.Height / 2;
                Draw.Text(g, items[i].Title, selected ? Theme.BodyStrong : Theme.Body,
                    new Rectangle(row.X + pad, row.Y + Px(5), textWidth, half - Px(3)), selected || i == hovered ? Theme.TextPrimary : Theme.TextSecondary);
                Draw.Text(g, When(items[i].Updated), Theme.Caption,
                    new Rectangle(row.X + pad, row.Y + half, textWidth, half - Px(5)), Theme.TextMuted);

                if (i == hovered)
                {
                    Draw.Text(g, Icons.Cancel, Theme.Icon(8f), CrossBounds(i), crossHovered ? Theme.TextPrimary : Theme.TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            }
        }

        /// <summary>"Today, 14:05", "Yesterday, 09:30" or "12 Nov, 16:20", in local time.</summary>
        private static string When(DateTime utc)
        {
            var british = System.Globalization.CultureInfo.GetCultureInfo("en-GB");
            var local = utc.ToLocalTime();
            int days = (DateTime.Now.Date - local.Date).Days;
            string day = days == 0 ? "Today" : days == 1 ? "Yesterday" : local.ToString("d MMM", british);
            return day + ", " + local.ToString("HH:mm", british);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = items.Count > 0 && e.Y >= 0 ? e.Y / RowHeight : -1;
            if (hit >= items.Count)
            {
                hit = -1;
            }
            bool overCross = hit >= 0 && CrossBounds(hit).Contains(e.Location);
            if (hit != hovered || overCross != crossHovered)
            {
                hovered = hit;
                crossHovered = overCross;
                Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = -1;
            crossHovered = false;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || hovered < 0 || hovered >= items.Count)
            {
                return;
            }

            var item = items[hovered];
            if (CrossBounds(hovered).Contains(e.Location))
            {
                DeleteClicked?.Invoke(this, item);
            }
            else
            {
                ConversationClicked?.Invoke(this, item);
            }
        }
    }
}
