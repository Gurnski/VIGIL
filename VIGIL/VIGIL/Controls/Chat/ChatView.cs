using VIGIL.Data;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Controls.Chat
{
    /// <summary>
    ///  A whole chat with the Detective: the messages, scrolling, above the box to type in.
    ///  The floating panel and the Detective page both use it. Set <see cref="Conversation"/>
    ///  to choose what it shows. <see cref="AskAbout"/> attaches a finding to the next question.
    ///
    ///  Set <c>BackColor</c> to the surface it sits on.
    /// </summary>
    [System.ComponentModel.DesignerCategory("Code")]
    public class ChatView : Panel
    {
        private readonly ChatService service;
        private readonly Panel scroller = new() { Dock = DockStyle.Fill, AutoScroll = true };
        private readonly ChatTranscript transcript = new();
        private readonly Panel footer = new() { Dock = DockStyle.Bottom };
        private readonly ContextChip chip = new() { Visible = false };
        private readonly ChatInput input = new();
        private Conversation conversation;
        private FindingSummary? about;

        /// <summary>Raised with the finding id when a cited finding is clicked.</summary>
        public event EventHandler<string>? CitationClicked;

        /// <summary>Raised when one of the Detective's lookups is clicked.</summary>
        public event EventHandler<AskStep>? StepClicked;

        public ChatView(ChatService service)
        {
            this.service = service;
            conversation = service.Start();
            DoubleBuffered = true;
            BackColor = Theme.Surface;

            transcript.SetSuggestions(service.Suggestions);
            transcript.SuggestionClicked += (s, question) => Send(question);
            transcript.CitationClicked += (s, id) => CitationClicked?.Invoke(this, id);
            transcript.StepClicked += (s, step) => StepClicked?.Invoke(this, step);
            scroller.Controls.Add(transcript);
            scroller.ClientSizeChanged += (s, e) => FitTranscript();
            transcript.SizeChanged += (s, e) => RemeasureScroller();

            chip.Removed += (s, e) => AskAbout(null);
            input.Submitted += (s, question) => Send(question);
            footer.Controls.Add(chip);
            footer.Controls.Add(input);
            footer.Layout += (s, e) => LayoutFooter();

            Controls.Add(scroller);
            Controls.Add(footer);

            service.ConversationChanged += Service_ConversationChanged;
            LayoutFooter();
            ShowConversation(false);
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        /// <summary>The conversation on show. Setting it drops any finding attached to the next question.</summary>
        public Conversation Conversation
        {
            get => conversation;
            set
            {
                conversation = value;
                about = null;
                chip.Visible = false;
                LayoutFooter();
                ShowConversation(true);
            }
        }

        /// <summary>Space left and right of the messages and the input. Written at 100% scale.</summary>
        public int Gutter { get; set; } = Theme.Space4;

        /// <summary>
        ///  The widest the messages and the input get, at 100% scale. Past about 80 characters
        ///  a line of text is hard to read, so on a wide page the chat stays a column on the left.
        /// </summary>
        public int MaxTextWidth { get; set; } = 760;

        /// <summary>Attaches a finding to the next question, or clears it with null.</summary>
        public void AskAbout(FindingSummary? finding)
        {
            about = finding;
            chip.Text = finding == null ? "" : finding.KindLabel + " in cell " + finding.CellId;
            chip.Visible = finding != null;
            LayoutFooter();
        }

        public void FocusInput() => input.FocusText();

        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);
            scroller.BackColor = BackColor;
            footer.BackColor = BackColor;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowChrome.UseDarkScrollbars(scroller);
            FitTranscript();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                service.ConversationChanged -= Service_ConversationChanged;
            }
            base.Dispose(disposing);
        }

        private void Send(string question)
        {
            if (service.IsWaiting(conversation))
            {
                return;
            }

            var finding = about;
            AskAbout(null);
            _ = service.AskAsync(conversation, question, finding);
        }

        private void Service_ConversationChanged(object? sender, Conversation changed)
        {
            if (changed.Id == conversation.Id && !IsDisposed)
            {
                ShowConversation(true);
            }
        }

        private void ShowConversation(bool scrollToEnd)
        {
            bool waiting = service.IsWaiting(conversation);
            input.Busy = waiting;
            transcript.SetMessages(conversation.Messages.ToList(), waiting);
            if (scrollToEnd)
            {
                scroller.AutoScrollPosition = new Point(0, transcript.Height);
            }
        }

        private void FitTranscript()
        {
            int gutter = Px(Gutter);
            int width = Math.Min(Px(MaxTextWidth), Math.Max(Px(120), scroller.ClientSize.Width - gutter * 2));
            if (transcript.Width != width || transcript.Left != gutter)
            {
                transcript.SetBounds(gutter, transcript.Top, width, transcript.Height, BoundsSpecified.X | BoundsSpecified.Width);
            }
        }

        private bool remeasureQueued;

        /// <summary>
        ///  The transcript changes its own height, often in the middle of the scroller's layout,
        ///  where the change is missed and a scrollbar is left behind for content that now fits.
        ///  This asks the scroller to measure again once the current layout has finished.
        /// </summary>
        private void RemeasureScroller()
        {
            if (remeasureQueued || !IsHandleCreated)
            {
                return;
            }

            remeasureQueued = true;
            BeginInvoke(() =>
            {
                remeasureQueued = false;
                if (!IsDisposed)
                {
                    scroller.PerformLayout();
                    FitTranscript();
                }
            });
        }

        private void LayoutFooter()
        {
            int gutter = Px(Gutter);
            int gap = Px(Theme.Space2);
            int width = Math.Min(Px(MaxTextWidth), Math.Max(Px(120), footer.ClientSize.Width - gutter * 2));
            int y = gap;
            if (chip.Visible)
            {
                chip.SetBounds(gutter, y, width, chip.Height);
                y += chip.Height + gap;
            }
            input.SetBounds(gutter, y, width, input.Height);
            int height = y + input.Height + Px(Theme.Space4);
            if (footer.Height != height)
            {
                footer.Height = height;
            }
        }
    }
}
