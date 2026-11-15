using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  The card shown while presenting: the address the audience opens to follow along, large
    ///  enough to read from the back of a room, with what following means and a way to stop.
    ///  It only shows what it is given and raises events. <c>MainForm</c> owns and places it.
    /// </summary>
    [System.ComponentModel.DesignerCategory("Code")]
    public class PresentPanel : Panel
    {
        private readonly VigilButton copy = new() { Variant = ButtonVariant.Secondary, IconGlyph = Icons.Copy, Text = "Copy the address" };
        private readonly VigilButton stop = new() { Variant = ButtonVariant.Ghost, Text = "Stop presenting" };
        private readonly VigilButton close = new() { Variant = ButtonVariant.Ghost, IconGlyph = Icons.Cancel, Text = "", TabStop = false };
        private readonly ToolTip tips = new();
        private IReadOnlyList<string> addresses = Array.Empty<string>();
        private string problem = "";
        private bool closedToNetwork;

        /// <summary>Raised when the presenter asks to stop.</summary>
        public event EventHandler? StopRequested;

        /// <summary>Raised when the card is closed. Presenting carries on.</summary>
        public event EventHandler? CloseRequested;

        public PresentPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Surface;
            copy.Click += (s, e) =>
            {
                if (addresses.Count > 0)
                {
                    Clipboard.SetText(addresses[0]);
                }
            };
            stop.Click += (s, e) => StopRequested?.Invoke(this, EventArgs.Empty);
            close.Click += (s, e) => CloseRequested?.Invoke(this, EventArgs.Empty);
            tips.SetToolTip(close, "Close. Presenting carries on.");
            Controls.Add(copy);
            Controls.Add(stop);
            Controls.Add(close);
        }

        /// <summary>
        ///  The addresses to show, the first being the one to read out. Empty when the machine is on no network.
        ///  <paramref name="openToNetwork"/> is the service's own word on whether other devices can reach it:
        ///  when it says no, the card says so in place of an address that would not answer.
        /// </summary>
        public void SetAddresses(IReadOnlyList<string> joinAddresses, bool? openToNetwork = null)
        {
            addresses = joinAddresses;
            closedToNetwork = openToNetwork == false;
            problem = "";
            copy.Visible = stop.Visible = true;
            copy.Enabled = addresses.Count > 0 && !closedToNetwork;
            Height = PreferredHeight();
            PerformLayout();
            Invalidate();
        }

        /// <summary>Shows a reason presenting could not start or has stopped, in place of the address.</summary>
        public void SetProblem(string message)
        {
            addresses = Array.Empty<string>();
            closedToNetwork = false;
            problem = message;
            // Nothing is being presented, so there is no address to copy and nothing to stop.
            copy.Visible = stop.Visible = false;
            copy.Enabled = false;
            Height = PreferredHeight();
            PerformLayout();
            Invalidate();
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        private int HeaderHeight => Px(44);

        private const string Lead = "Anyone on this network can follow along. They open:";
        private const string NoNetwork = "This machine is on no network that others could reach it through.";
        private const string Closed = "The analysis service is listening on this machine only, so nobody else can open it yet. "
            + "Stop the service, start it again with --host 0.0.0.0 added to the command, and press Present once more.";
        private const string Note = "Their screen shows the finding, the layer and the zone you choose, and they can stop following at any time. "
            + "The service must have been started with --host 0.0.0.0, or only this machine can reach it.";

        private int PreferredHeight()
        {
            using var g = CreateGraphics();
            return Arrange(g, false);
        }

        /// <summary>Lays the card out top to bottom and returns its height. With <paramref name="paint"/>, draws it too.</summary>
        private int Arrange(Graphics g, bool paint)
        {
            int edge = Px(Theme.Space4);
            int width = Math.Max(1, Width - edge * 2);
            int y = HeaderHeight + Px(Theme.Space3);
            const TextFormatFlags wrapped = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top;

            int Paragraph(string text, Font font, Color colour, int gapAfter)
            {
                int height = TextRenderer.MeasureText(g, text, font, new Size(width, int.MaxValue), wrapped).Height;
                if (paint)
                {
                    Draw.Text(g, text, font, new Rectangle(edge, y, width, height), colour, wrapped);
                }
                return y + height + Px(gapAfter);
            }

            if (problem.Length > 0)
            {
                y = Paragraph(problem, Theme.Body, Theme.Warning, Theme.Space4);
            }
            else if (closedToNetwork)
            {
                y = Paragraph(Closed, Theme.Body, Theme.Warning, Theme.Space4);
            }
            else if (addresses.Count == 0)
            {
                y = Paragraph(NoNetwork, Theme.Body, Theme.Warning, Theme.Space4);
            }
            else
            {
                y = Paragraph(Lead, Theme.Body, Theme.TextSecondary, Theme.Space2);
                for (int i = 0; i < addresses.Count; i++)
                {
                    // The first is the one to read out. Others are this machine's other networks.
                    y = Paragraph(addresses[i], i == 0 ? Theme.Heading : Theme.Numeric, i == 0 ? Theme.TextPrimary : Theme.TextSecondary,
                        i == addresses.Count - 1 ? Theme.Space4 : Theme.Space1);
                }
                y = Paragraph(Note, Theme.Caption, Theme.TextMuted, Theme.Space4);
            }

            if (problem.Length > 0)
            {
                return y;                                // the reason is the whole card
            }
            int buttonHeight = Px(Theme.ControlHeight);
            if (!paint)
            {
                copy.Bounds = new Rectangle(edge, y, Px(172), buttonHeight);
                stop.Bounds = new Rectangle(copy.Right + Px(Theme.Space2), y, Px(140), buttonHeight);
            }
            return y + buttonHeight + Px(Theme.Space4);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int size = Px(Theme.ControlHeightSmall);
            close.Bounds = new Rectangle(Width - Px(Theme.Space2) - size, (HeaderHeight - size) / 2, size, size);
            if (IsHandleCreated && Width > 0)
            {
                using var g = CreateGraphics();
                Arrange(g, false);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Text(g, "Presenting", Theme.BodyStrong, new Rectangle(Px(Theme.Space4), 0, close.Left - Px(Theme.Space4), HeaderHeight), Theme.TextPrimary);
            Draw.HorizontalLine(g, 1, Width - 2, HeaderHeight - 1, Theme.Divider);
            Arrange(g, true);
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
