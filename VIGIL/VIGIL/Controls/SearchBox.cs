using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  Rounded text box with a search icon and placeholder. The outline turns the accent
    ///  colour while it has focus.
    /// </summary>
    [DefaultEvent(nameof(TextChanged))]
    public class SearchBox : UserControl
    {
        // Initialised before the base constructor runs, which may read Text.
        private readonly TextBox textBox = new()
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.SurfaceRaised,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Body,
            PlaceholderText = "Search…"
        };

        public SearchBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            Cursor = Cursors.IBeam;
            textBox.GotFocus += (s, e) => Invalidate();
            textBox.LostFocus += (s, e) => Invalidate();
            textBox.TextChanged += (s, e) => OnTextChanged(e);
            Controls.Add(textBox);
        }

        protected override Size DefaultSize => Theme.Scaled(280, Theme.ControlHeight);

        [Browsable(true), EditorBrowsable(EditorBrowsableState.Always)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        [AllowNull]
        public override string Text
        {
            get => textBox.Text;
            set => textBox.Text = value ?? "";
        }

        [Browsable(true), EditorBrowsable(EditorBrowsableState.Always)]
        public new event EventHandler? TextChanged
        {
            add => base.TextChanged += value;
            remove => base.TextChanged -= value;
        }

        [DefaultValue("Search…")]
        public string Placeholder
        {
            get => textBox.PlaceholderText;
            set => textBox.PlaceholderText = value;
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        // Icon column: Space3 in from the edge, Space5 wide, then Space1 before the text.
        private int IconLeft => Px(Theme.Space3);
        private int TextLeft => Px(Theme.Space3 + Theme.Space5 + Theme.Space1);

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            textBox.SetBounds(TextLeft, (Height - textBox.Height) / 2, Math.Max(0, Width - TextLeft - Px(Theme.Space3)), textBox.Height);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            textBox.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            bool focused = textBox.Focused;
            Draw.RoundedBox(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Px(Theme.RadiusSmall),
                Theme.SurfaceRaised, focused ? Theme.Accent : Theme.Border);
            Draw.Text(g, Icons.Search, Theme.Icon(10f), new Rectangle(IconLeft, 0, Px(Theme.Space5), Height),
                focused ? Theme.Accent : Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }
}
