using VIGIL.Data;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Controls.Chat
{
    /// <summary>
    ///  The Detective in a small panel that floats over whichever page is open. It has a
    ///  title bar with buttons to start a new chat, open the chat on the Detective page, and
    ///  close the panel, above a <see cref="ChatView"/>. <c>MainForm</c> owns and places it.
    /// </summary>
    [System.ComponentModel.DesignerCategory("Code")]
    public class ChatPanel : Panel
    {
        private readonly ChatService service;
        private readonly ChatView view;
        private readonly VigilButton newChat = IconButton(Icons.Add);
        private readonly VigilButton expand = IconButton(Icons.FullScreen);
        private readonly VigilButton close = IconButton(Icons.Cancel);
        private readonly ToolTip tips = new();

        /// <summary>Raised when the user asks to carry on in the Detective page.</summary>
        public event EventHandler? ExpandRequested;

        /// <summary>Raised when the user closes the panel.</summary>
        public event EventHandler? CloseRequested;

        /// <summary>Raised with the finding id when a cited finding is clicked.</summary>
        public event EventHandler<string>? CitationClicked;

        /// <summary>Raised when one of the Detective's lookups is clicked.</summary>
        public event EventHandler<AskStep>? StepClicked;

        public ChatPanel(ChatService service)
        {
            this.service = service;
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Surface;

            view = new ChatView(service) { BackColor = Theme.Surface };
            view.CitationClicked += (s, id) => CitationClicked?.Invoke(this, id);
            view.StepClicked += (s, step) => StepClicked?.Invoke(this, step);
            Controls.Add(view);

            newChat.Click += (s, e) => view.Conversation = service.Start();
            expand.Click += (s, e) => ExpandRequested?.Invoke(this, EventArgs.Empty);
            close.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
            tips.SetToolTip(newChat, "New chat");
            tips.SetToolTip(expand, "Open in the Detective page");
            tips.SetToolTip(close, "Close");
            Controls.Add(newChat);
            Controls.Add(expand);
            Controls.Add(close);
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        private int HeaderHeight => Px(44);

        /// <summary>The conversation the panel is showing.</summary>
        public Conversation Conversation
        {
            get => view.Conversation;
            set => view.Conversation = value;
        }

        /// <summary>Attaches a finding to the next question.</summary>
        public void AskAbout(FindingSummary finding) => view.AskAbout(finding);

        public void FocusInput() => view.FocusInput();

        private static VigilButton IconButton(string glyph) => new()
        {
            Variant = ButtonVariant.Ghost,
            IconGlyph = glyph,
            Text = "",
            TabStop = false
        };

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int size = Px(Theme.ControlHeightSmall);
            int y = (HeaderHeight - size) / 2;
            int x = Width - Px(Theme.Space2) - size;
            foreach (var button in new[] { close, expand, newChat })
            {
                button.Bounds = new Rectangle(x, y, size, size);
                x -= size + Px(Theme.Space1);
            }
            // One pixel in from each edge, so the outline drawn in OnPaint stays visible.
            view.Bounds = new Rectangle(1, HeaderHeight, Math.Max(0, Width - 2), Math.Max(0, Height - HeaderHeight - 1));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Text(g, "Detective", Theme.BodyStrong, new Rectangle(Px(Theme.Space4), 0, newChat.Left - Px(Theme.Space4), HeaderHeight), Theme.TextPrimary);
            Draw.HorizontalLine(g, 1, Width - 2, HeaderHeight - 1, Theme.Divider);
            using var pen = new Pen(Theme.Border);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
