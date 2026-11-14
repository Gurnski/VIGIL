using System.ComponentModel;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  Left-hand navigation: the VIGIL mark at the top, a list of pages with a sliding
    ///  selection pill, a collapse toggle and a footer line. Collapsed, it shows icons only
    ///  with tooltips. It sets its own Width, so a Dock = Left host follows it.
    /// </summary>
    [DefaultEvent(nameof(SelectedChanged))]
    public class NavRail : VigilControl
    {
        private sealed record Item(string Key, string Glyph, string Text);

        private const string Tagline = "Earth system trend detective";
        private const int ToggleHit = -2;
        private const TextFormatFlags Centred =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        private readonly List<Item> items = new();
        private readonly Transition indicator;
        private readonly Transition collapse;
        private readonly ToolTip toolTip = new() { InitialDelay = 300, ReshowDelay = 100 };
        private int selected = -1;
        private int hovered = -1;
        private bool collapsed;
        private string footer = "University of Plymouth · NASA Space Apps 2026";

        public event EventHandler? SelectedChanged;
        public event EventHandler? CollapsedChanged;

        public NavRail()
        {
            BackColor = Theme.SurfaceLow;
            indicator = new Transition(Invalidate) { Speed = 0.25f };
            collapse = new Transition(ApplyWidth) { Speed = 0.22f };
        }

        protected override Size DefaultSize => new(Theme.RailWidth, 600);

        [DefaultValue("University of Plymouth · NASA Space Apps 2026")]
        public string Footer
        {
            get => footer;
            set { footer = value ?? ""; Invalidate(); }
        }

        /// <summary>
        ///  Icons only, at <see cref="Theme.RailCollapsedWidth"/>. Animates the width when changed at runtime.
        /// </summary>
        [DefaultValue(false)]
        public bool Collapsed
        {
            get => collapsed;
            set
            {
                if (collapsed == value)
                {
                    return;
                }

                collapsed = value;
                if (IsHandleCreated)
                {
                    collapse.To(value ? 1f : 0f);
                }
                else
                {
                    collapse.Jump(value ? 1f : 0f);
                }
                CollapsedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [Browsable(false)]
        public string? SelectedKey => selected >= 0 ? items[selected].Key : null;

        public void AddItem(string key, string glyph, string text)
        {
            items.Add(new Item(key, glyph, text));
            Invalidate();
        }

        public void Select(string key)
        {
            int index = items.FindIndex(i => i.Key == key);
            if (index < 0 || index == selected)
            {
                return;
            }

            if (selected < 0)
            {
                indicator.Jump(index);
            }
            else
            {
                indicator.To(index);
            }
            selected = index;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyWidth()
        {
            int expanded = Px(Theme.RailWidth);
            int narrow = Px(Theme.RailCollapsedWidth);
            Width = (int)Math.Round(expanded + (narrow - expanded) * collapse.Value);
            Invalidate();
        }

        // Layout, top to bottom: brand mark, items, then the toggle and footer pinned to the bottom.
        private int Inset => Px(Theme.Space3);
        private int ItemsTop => Px(88);
        private int ItemHeight => Px(40);
        private int ItemPitch => Px(44);
        private int MarkSize => Px(28);
        private float Expanded => 1f - collapse.Value;

        private Rectangle ItemBounds(float index) =>
            new(Inset, ItemsTop + (int)Math.Round(index * ItemPitch), Width - Inset * 2, ItemHeight);

        private Rectangle FooterBounds =>
            new(Px(Theme.Space5), Height - Px(Theme.Space4) - Px(32), Width - Px(Theme.Space5) - Px(Theme.Space4), Px(32));

        private Rectangle ToggleBounds
        {
            get
            {
                int footerSpace = footer.Length > 0 ? (int)Math.Round((Px(32) + Px(Theme.Space3)) * Expanded) : 0;
                return new Rectangle(Inset, Height - Px(Theme.Space4) - footerSpace - ItemHeight, Width - Inset * 2, ItemHeight);
            }
        }

        private int HitTest(Point location)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (ItemBounds(i).Contains(location))
                {
                    return i;
                }
            }
            return ToggleBounds.Contains(location) ? ToggleHit : -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            DrawBrand(g);

            if (hovered >= 0 && hovered != selected)
            {
                Draw.RoundedBox(g, ItemBounds(hovered), Px(Theme.RadiusSmall), Draw.WithAlpha(Theme.SurfaceHover, 120));
            }
            if (selected >= 0)
            {
                Draw.RoundedBox(g, ItemBounds(indicator.Value), Px(Theme.RadiusSmall), Draw.WithAlpha(Theme.Accent, 22));
            }

            for (int i = 0; i < items.Count; i++)
            {
                bool isSelected = i == selected;
                Color iconColour = isSelected ? Theme.Accent : i == hovered ? Theme.TextPrimary : Theme.TextSecondary;
                Color textColour = isSelected || i == hovered ? Theme.TextPrimary : Theme.TextSecondary;
                DrawRow(g, ItemBounds(i), items[i].Glyph, items[i].Text, isSelected ? Theme.BodyStrong : Theme.Body, iconColour, textColour);
            }

            var toggle = ToggleBounds;
            if (hovered == ToggleHit)
            {
                Draw.RoundedBox(g, toggle, Px(Theme.RadiusSmall), Draw.WithAlpha(Theme.SurfaceHover, 120));
            }
            Color toggleColour = hovered == ToggleHit ? Theme.TextPrimary : Theme.TextMuted;
            DrawRow(g, toggle, collapsed ? Icons.OpenPane : Icons.ClosePane, "Collapse", Theme.Body, toggleColour, toggleColour);

            if (footer.Length > 0 && Expanded > 0.5f)
            {
                Draw.Text(g, footer, Theme.Caption, FooterBounds, Draw.Blend(BackColor, Theme.TextMuted, Expanded),
                    TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        private void DrawBrand(Graphics g)
        {
            int markLeft = Px(Theme.Space5);
            int markX = (int)Math.Round(markLeft * Expanded + (Width - MarkSize) / 2f * collapse.Value);
            var mark = new Rectangle(markX, Px(Theme.Space6), MarkSize, MarkSize);
            Draw.RoundedBox(g, mark, Px(Theme.Space2), Theme.Accent);
            Draw.Text(g, Icons.Globe, Theme.Icon(13f), mark, Theme.OnAccent, Centred);

            if (Expanded > 0.3f)
            {
                // Wordmark and tagline as one block, centred on the mark.
                int textX = markLeft + MarkSize + Px(Theme.Space3);
                int textWidth = Width - textX - Px(Theme.Space3);
                int nameHeight = Draw.Measure(g, "VIGIL", Theme.Title).Height;
                int tagHeight = Draw.Measure(g, Tagline, Theme.Caption).Height;
                int top = mark.Y + (mark.Height - nameHeight - tagHeight) / 2;
                Draw.Text(g, "VIGIL", Theme.Title, new Rectangle(textX, top, textWidth, nameHeight),
                    Draw.Blend(BackColor, Theme.TextPrimary, Expanded));
                Draw.Text(g, Tagline, Theme.Caption, new Rectangle(textX, top + nameHeight, textWidth, tagHeight),
                    Draw.Blend(BackColor, Theme.TextMuted, Expanded));
            }
        }

        /// <summary>
        ///  An icon and label row. The icon slides to the centre and the label fades as the rail collapses.
        /// </summary>
        private void DrawRow(Graphics g, Rectangle bounds, string glyph, string text, Font font, Color iconColour, Color textColour)
        {
            int iconWidth = Px(Theme.Space5);
            int iconX = (int)Math.Round((bounds.X + Px(Theme.Space3)) * Expanded + (bounds.X + (bounds.Width - iconWidth) / 2f) * collapse.Value);
            Draw.Text(g, glyph, Theme.Icon(12f), new Rectangle(iconX, bounds.Y, iconWidth, bounds.Height), iconColour, Centred);

            int textX = bounds.X + Px(Theme.Space10 + Theme.Space1);
            int textWidth = bounds.Right - Px(Theme.Space2) - textX;
            if (Expanded > 0.3f && textWidth > Px(Theme.Space10))
            {
                Draw.Text(g, text, font, new Rectangle(textX, bounds.Y, textWidth, bounds.Height), Draw.Blend(BackColor, textColour, Expanded));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitTest(e.Location);
            Cursor = hit != -1 ? Cursors.Hand : Cursors.Default;
            if (hit == hovered)
            {
                return;
            }

            hovered = hit;
            Invalidate();
            toolTip.Hide(this);
            if (collapsed && hit != -1)
            {
                var bounds = hit == ToggleHit ? ToggleBounds : ItemBounds(hit);
                string text = hit == ToggleHit ? "Expand" : items[hit].Text;
                toolTip.Show(text, this, bounds.Right + Px(Theme.Space2), bounds.Y + Px(Theme.Space2), 1500);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = -1;
            toolTip.Hide(this);
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int hit = HitTest(e.Location);
            if (hit == ToggleHit)
            {
                toolTip.Hide(this);
                Collapsed = !Collapsed;
            }
            else if (hit >= 0)
            {
                Select(items[hit].Key);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                indicator.Dispose();
                collapse.Dispose();
                toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
