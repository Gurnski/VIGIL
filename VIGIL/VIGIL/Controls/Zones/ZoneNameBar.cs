using VIGIL.Theming;

namespace VIGIL.Controls.Zones
{
    /// <summary>
    ///  The small panel shown over the globe when an outline has just been drawn: a name to
    ///  type, how many grid cells the outline takes in, and Save and Discard. Enter saves and
    ///  Escape discards. It sets its own height.
    /// </summary>
    [System.ComponentModel.DesignerCategory("Code")]
    public class ZoneNameBar : UserControl
    {
        // Initialised before the base constructor runs, as in SearchBox.
        private readonly TextBox textBox = new()
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.SurfaceRaised,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Body,
            MaxLength = 60,
            PlaceholderText = "Name"
        };
        private readonly VigilButton save = new() { Text = "Save zone", Variant = ButtonVariant.Primary };
        private readonly VigilButton discard = new() { Text = "Discard", Variant = ButtonVariant.Ghost };
        private string detail = "";

        /// <summary>Raised by the Save button or the Enter key.</summary>
        public event EventHandler? SaveRequested;

        /// <summary>Raised by the Discard button or the Escape key.</summary>
        public event EventHandler? DiscardRequested;

        public ZoneNameBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            textBox.GotFocus += (s, e) => Invalidate();
            textBox.LostFocus += (s, e) => Invalidate();
            textBox.KeyDown += TextBox_KeyDown;
            save.Click += (s, e) => SaveRequested?.Invoke(this, EventArgs.Empty);
            discard.Click += (s, e) => DiscardRequested?.Invoke(this, EventArgs.Empty);
            Controls.Add(textBox);
            Controls.Add(save);
            Controls.Add(discard);
        }

        protected override Size DefaultSize => Theme.Scaled(320, 140);

        /// <summary>The name as typed, with the spaces at its ends removed.</summary>
        public string ZoneName => textBox.Text.Trim();

        /// <summary>Fills the bar for a new outline: a suggested name and a line about what it takes in.</summary>
        public void Begin(string suggestedName, string detail)
        {
            textBox.Text = suggestedName;
            this.detail = detail;
            Invalidate();
        }

        /// <summary>Puts the cursor in the name with all of it selected, so typing replaces the suggestion.</summary>
        public void FocusName()
        {
            textBox.Focus();
            textBox.SelectAll();
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        private int Pad => Px(Theme.Space4);
        private Rectangle TitleBounds => new(Pad, Pad, Width - Pad * 2, Px(16));
        private Rectangle InputBounds => new(Pad, TitleBounds.Bottom + Px(Theme.Space2), Width - Pad * 2, Px(Theme.ControlHeight));

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var input = InputBounds;
            int inset = Px(Theme.Space3);
            textBox.SetBounds(input.X + inset, input.Y + (input.Height - textBox.Height) / 2, Math.Max(0, input.Width - inset * 2), textBox.Height);

            int top = input.Bottom + Px(Theme.Space3);
            int buttonHeight = Px(Theme.ControlHeight);
            save.SetBounds(Width - Pad - Px(108), top, Px(108), buttonHeight);
            discard.SetBounds(save.Left - Px(Theme.Space2) - Px(88), top, Px(88), buttonHeight);

            int height = top + buttonHeight + Pad;
            if (Height != height)
            {
                Height = height;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            // A panel over the globe is one of the few places that takes an outline.
            Draw.RoundedBox(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Px(Theme.Radius), Theme.Surface, Theme.Border);
            Draw.Caption(g, "New zone", TitleBounds, Theme.TextSecondary);
            Draw.Text(g, detail, Theme.Caption, TitleBounds, Theme.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            var input = InputBounds;
            Draw.RoundedBox(g, new RectangleF(input.X + 0.5f, input.Y + 0.5f, input.Width - 1f, input.Height - 1f), Px(Theme.RadiusSmall),
                Theme.SurfaceRaised, textBox.Focused ? Theme.Accent : Theme.Border);
        }

        private void TextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SaveRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                DiscardRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
