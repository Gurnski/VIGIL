using System.ComponentModel;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    public enum ButtonVariant
    {
        /// <summary>Filled with the accent colour. One per screen, for the main action.</summary>
        Primary,
        /// <summary>Raised surface with a fine outline, for everyday actions.</summary>
        Secondary,
        /// <summary>No fill until hovered, for low-priority actions.</summary>
        Ghost
    }

    /// <summary>
    ///  Rounded button with animated hover and press states and an optional icon glyph.
    /// </summary>
    [DefaultEvent(nameof(Click))]
    public class VigilButton : VigilControl
    {
        private readonly Transition hover;
        private readonly Transition press;
        private ButtonVariant variant = ButtonVariant.Primary;
        private string iconGlyph = "";

        public VigilButton()
        {
            hover = new Transition(Invalidate);
            press = new Transition(Invalidate) { Speed = 0.35f };
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.BodyStrong;
        }

        protected override Size DefaultSize => new(120, Theme.ControlHeight);

        [DefaultValue(ButtonVariant.Primary)]
        public ButtonVariant Variant
        {
            get => variant;
            set { variant = value; Invalidate(); }
        }

        /// <summary>
        ///  A glyph from <see cref="Icons"/>, drawn before the text. Empty for none.
        /// </summary>
        [DefaultValue("")]
        public string IconGlyph
        {
            get => iconGlyph;
            set { iconGlyph = value ?? ""; Invalidate(); }
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hover.To(1f);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover.To(0f);
            press.To(0f);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                press.To(1f);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            press.To(0f);
        }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            var (fill, text, border) = Colours();
            float radius = Px(Theme.RadiusSmall);
            if (border is Color)
            {
                Draw.RoundedBox(g, OutlineBounds, radius, fill, border);
            }
            else
            {
                Draw.RoundedBox(g, new Rectangle(0, 0, Width, Height), radius, fill);
            }

            if (Focused && ShowFocusCues)
            {
                using var path = Draw.RoundedRect(RectangleF.Inflate(OutlineBounds, -Px(2), -Px(2)), radius - Px(2));
                using var ring = new Pen(variant == ButtonVariant.Primary ? Theme.OnAccent : Theme.Accent, 1f);
                g.DrawPath(ring, path);
            }

            var iconFont = Theme.Icon(Font.SizeInPoints);
            var iconSize = iconGlyph.Length > 0 ? Draw.Measure(g, iconGlyph, iconFont) : Size.Empty;
            var textSize = Text.Length > 0 ? Draw.Measure(g, Text, Font) : Size.Empty;
            int gap = iconGlyph.Length > 0 && Text.Length > 0 ? Px(Theme.Space2) : 0;
            int x = (Width - (iconSize.Width + gap + textSize.Width)) / 2;

            if (iconGlyph.Length > 0)
            {
                Draw.Text(g, iconGlyph, iconFont, new Rectangle(x, 0, iconSize.Width, Height), text);
            }
            if (Text.Length > 0)
            {
                Draw.Text(g, Text, Font, new Rectangle(x + iconSize.Width + gap, 0, textSize.Width + 1, Height), text);
            }
        }

        private (Color Fill, Color Text, Color? Border) Colours()
        {
            if (!Enabled)
            {
                return variant == ButtonVariant.Ghost
                    ? (Color.Transparent, Theme.TextDisabled, null)
                    : (Theme.SurfaceRaised, Theme.TextDisabled, null);
            }

            float h = hover.Value;
            float p = press.Value;
            return variant switch
            {
                ButtonVariant.Primary => (
                    Draw.Blend(Draw.Blend(Theme.Accent, Theme.AccentHover, h), Theme.AccentPressed, p),
                    Theme.OnAccent,
                    null),
                ButtonVariant.Secondary => (
                    Draw.Blend(Draw.Blend(Theme.SurfaceRaised, Theme.SurfaceHover, h), Theme.Border, p),
                    Theme.TextPrimary,
                    Theme.Border),
                _ => (
                    Draw.WithAlpha(Theme.SurfaceHover, (int)(255 * Math.Max(h, p))),
                    Draw.Blend(Theme.TextSecondary, Theme.TextPrimary, h),
                    null)
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hover.Dispose();
                press.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
