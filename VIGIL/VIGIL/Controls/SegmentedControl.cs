using System.ComponentModel;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  A row of options where exactly one is selected, such as "Monthly | Annual".
    ///  Call <see cref="SetItems"/> to fill it; it sizes itself so every segment is as wide as the widest label.
    /// </summary>
    [DefaultEvent(nameof(SelectedChanged))]
    public class SegmentedControl : VigilControl
    {
        private const TextFormatFlags Centred =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding
            | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        private readonly Transition indicator;
        private string[] items = Array.Empty<string>();
        private int selected = -1;
        private int hovered = -1;

        public event EventHandler? SelectedChanged;

        public SegmentedControl()
        {
            indicator = new Transition(Invalidate) { Speed = 0.3f };
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.Body;
        }

        protected override Size DefaultSize => Theme.Scaled(240, 32);

        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<string> Items => items;

        /// <summary>
        ///  Replaces the options and selects the first one if nothing was selected.
        /// </summary>
        public void SetItems(params string[] values)
        {
            items = values ?? Array.Empty<string>();
            selected = items.Length > 0 ? Math.Clamp(selected, 0, items.Length - 1) : -1;
            indicator.Jump(Math.Max(selected, 0));
            FitToItems();
            Invalidate();
        }

        [DefaultValue(-1)]
        public int SelectedIndex
        {
            get => selected;
            set
            {
                if (value == selected || value < -1 || value >= items.Length)
                {
                    return;
                }

                selected = value;
                if (value >= 0)
                {
                    if (IsHandleCreated)
                    {
                        indicator.To(value);
                    }
                    else
                    {
                        indicator.Jump(value);
                    }
                }
                Invalidate();
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [Browsable(false)]
        public string? SelectedItem => selected >= 0 ? items[selected] : null;

        private int Inset => Px(3);

        /// <summary>
        ///  Sets the width so every segment is as wide as the widest label plus padding.
        /// </summary>
        public void FitToItems()
        {
            if (items.Length == 0)
            {
                return;
            }

            int widest = items.Max(item => TextRenderer.MeasureText(item, Font, Size.Empty, TextFormatFlags.NoPadding).Width);
            Width = Inset * 2 + items.Length * (widest + Px(Theme.Space4) * 2);
        }

        private RectangleF SegmentBounds(float index)
        {
            float width = (Width - Inset * 2) / (float)Math.Max(1, items.Length);
            return new RectangleF(Inset + index * width, Inset, width, Height - Inset * 2);
        }

        private int HitTest(Point location)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (SegmentBounds(i).Contains(location))
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitToItems(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); FitToItems(); }

        /// <summary>
        ///  Never shorter than its labels, nor narrower than they need with a little padding,
        ///  so a label is not cut off whatever size a page gives the control.
        /// </summary>
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            int minHeight = Font.Height + Px(Theme.Space2) + Inset * 2;
            int minWidth = 0;
            if (items.Length > 0)
            {
                int widest = items.Max(item => TextRenderer.MeasureText(item, Font, Size.Empty, TextFormatFlags.NoPadding).Width);
                minWidth = Inset * 2 + items.Length * (widest + Px(Theme.Space2) * 2);
            }
            base.SetBoundsCore(x, y, Math.Max(width, minWidth), Math.Max(height, minHeight), specified);
        }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            float radius = Px(Theme.RadiusSmall);
            Draw.RoundedBox(g, new Rectangle(0, 0, Width, Height), radius + Inset, Theme.SurfaceRaised);

            if (Enabled && hovered >= 0 && hovered != selected)
            {
                Draw.RoundedBox(g, SegmentBounds(hovered), radius, Draw.Blend(Theme.SurfaceRaised, Theme.SurfaceHover, 0.45f));
            }
            if (selected >= 0)
            {
                Draw.RoundedBox(g, SegmentBounds(indicator.Value), radius, Enabled ? Theme.SurfaceHover : Theme.Border);
            }

            if (Focused && ShowFocusCues)
            {
                using var path = Draw.RoundedRect(RectangleF.Inflate(OutlineBounds, -1f, -1f), radius + Inset - 1f);
                using var ring = new Pen(Theme.TextSecondary, 1f);
                g.DrawPath(ring, path);
            }

            for (int i = 0; i < items.Length; i++)
            {
                Color colour = !Enabled ? Theme.TextDisabled
                    : i == selected || i == hovered ? Theme.TextPrimary
                    : Theme.TextSecondary;
                Draw.Text(g, items[i], Font, Rectangle.Round(SegmentBounds(i)), colour, Centred);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitTest(e.Location);
            if (hit != hovered)
            {
                hovered = hit;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hovered = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                int hit = HitTest(e.Location);
                if (hit >= 0)
                {
                    SelectedIndex = hit;
                }
            }
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (items.Length == 0)
            {
                return;
            }

            if (e.KeyCode == Keys.Left)
            {
                SelectedIndex = Math.Max(0, selected - 1);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right)
            {
                SelectedIndex = Math.Min(items.Length - 1, selected + 1);
                e.Handled = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                indicator.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
