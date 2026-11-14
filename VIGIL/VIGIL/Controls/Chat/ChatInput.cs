using VIGIL.Theming;

namespace VIGIL.Controls.Chat
{
    /// <summary>
    ///  The box a question is typed into, with a send button on its right. Enter sends and
    ///  Shift+Enter starts a new line. While <see cref="Busy"/> is set, nothing can be sent.
    /// </summary>
    public class ChatInput : VigilControl
    {
        private readonly TextBox textBox = new()
        {
            BorderStyle = BorderStyle.None,
            Multiline = true,
            BackColor = Theme.SurfaceRaised,
            ForeColor = Theme.TextPrimary,
            Font = Theme.Body,
            PlaceholderText = "Ask about the findings…"
        };
        private readonly VigilButton send = new() { Variant = ButtonVariant.Primary, IconGlyph = Icons.Send, Text = "", TabStop = false };
        private bool busy;

        /// <summary>Raised with the trimmed text when the user sends a question. The box is cleared first.</summary>
        public event EventHandler<string>? Submitted;

        public ChatInput()
        {
            Cursor = Cursors.IBeam;
            textBox.GotFocus += (s, e) => Invalidate();
            textBox.LostFocus += (s, e) => Invalidate();
            textBox.TextChanged += (s, e) => UpdateSend();
            textBox.KeyDown += TextBox_KeyDown;
            send.Click += (s, e) => Submit();
            Controls.Add(textBox);
            Controls.Add(send);
            UpdateSend();
        }

        protected override Size DefaultSize => Theme.Scaled(320, 60);

        /// <summary>True while an answer is on its way. Typing still works, but sending waits.</summary>
        public bool Busy
        {
            get => busy;
            set
            {
                busy = value;
                UpdateSend();
            }
        }

        public string Placeholder
        {
            get => textBox.PlaceholderText;
            set => textBox.PlaceholderText = value;
        }

        public void FocusText() => textBox.Focus();

        private void UpdateSend() => send.Enabled = !busy && textBox.Text.Trim().Length > 0;

        private void TextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                Submit();
            }
        }

        private void Submit()
        {
            string text = textBox.Text.Trim();
            if (busy || text.Length == 0)
            {
                return;
            }

            textBox.Clear();
            Submitted?.Invoke(this, text);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Px(Theme.Space3);
            int button = Px(Theme.ControlHeight);
            send.Bounds = new Rectangle(Width - pad - button, Height - pad - button, button, button);
            textBox.Bounds = new Rectangle(pad, pad, Math.Max(Px(40), send.Left - pad * 2), Math.Max(Px(16), Height - pad * 2));
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            textBox.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Draw.Smooth(e.Graphics);
            Draw.RoundedBox(e.Graphics, OutlineBounds, Px(Theme.Radius), Theme.SurfaceRaised,
                textBox.Focused ? Theme.TextSecondary : Theme.Border);
        }
    }
}
