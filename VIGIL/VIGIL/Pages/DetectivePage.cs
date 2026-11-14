using VIGIL.Controls;
using VIGIL.Controls.Chat;
using VIGIL.Data;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Pages
{
    /// <summary>
    ///  The Detective page: every saved chat in a list on the left, and the chosen one at
    ///  full size on the right. It shares its chats with the floating panel through
    ///  <see cref="ChatService"/>, so a chat started in one carries on in the other.
    /// </summary>
    // Built in code with no designer file, so Visual Studio opens it as code rather than trying to load a designer.
    [System.ComponentModel.DesignerCategory("Code")]
    public class DetectivePage : UserControl
    {
        private readonly ChatService service;
        private readonly PageHeader header = new() { Title = "Detective", Dock = DockStyle.Top };
        private readonly Panel body = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
        private readonly VigilButton newChat = new() { Variant = ButtonVariant.Secondary, IconGlyph = Icons.Add, Text = "New chat" };
        private readonly Panel listScroller = new() { AutoScroll = true, BackColor = Theme.Background };
        private readonly ConversationList list = new();
        private readonly ChatView view;

        /// <summary>Raised with the finding id when a cited finding is clicked.</summary>
        public event EventHandler<string>? CitationClicked;

        public DetectivePage(ChatService service)
        {
            this.service = service;
            BackColor = Theme.Background;
            DoubleBuffered = true;

            header.Height = Px(72);
            view = new ChatView(service) { BackColor = Theme.Surface, Gutter = Theme.Space6 };
            view.CitationClicked += (s, id) => CitationClicked?.Invoke(this, id);

            listScroller.Controls.Add(list);
            listScroller.ClientSizeChanged += (s, e) => list.Width = listScroller.ClientSize.Width;
            body.Controls.Add(newChat);
            body.Controls.Add(listScroller);
            body.Controls.Add(view);
            body.Layout += (s, e) => LayoutBody();
            Controls.Add(body);
            Controls.Add(header);

            newChat.Click += (s, e) => Open(service.Start());
            list.ConversationClicked += (s, conversation) => Open(conversation);
            list.DeleteClicked += List_DeleteClicked;
            service.ListChanged += Service_Changed;
            service.ConversationChanged += Service_ConversationChanged;

            RefreshList();
            UpdateSubtitle();
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        /// <summary>Shows a conversation in the large view.</summary>
        public void Open(Conversation conversation)
        {
            view.Conversation = conversation;
            RefreshList();
            view.FocusInput();
        }

        /// <summary>The conversation in the large view.</summary>
        public Conversation Current => view.Conversation;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowChrome.UseDarkScrollbars(listScroller);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                service.ListChanged -= Service_Changed;
                service.ConversationChanged -= Service_ConversationChanged;
            }
            base.Dispose(disposing);
        }

        private void Service_Changed(object? sender, EventArgs e) => RefreshList();

        private void Service_ConversationChanged(object? sender, Conversation e)
        {
            RefreshList();
            UpdateSubtitle();
        }

        private void List_DeleteClicked(object? sender, Conversation conversation)
        {
            bool wasOpen = conversation.Id == view.Conversation.Id;
            service.Delete(conversation);
            if (wasOpen)
            {
                view.Conversation = service.Start();
                RefreshList();
            }
        }

        private void RefreshList() => list.SetItems(service.Conversations, view.Conversation.Id);

        private void UpdateSubtitle()
        {
            header.Subtitle = service.Connected switch
            {
                true => "Answers come from the scan's findings, through the analysis service.",
                false => "The analysis service at " + service.ServiceAddress + " isn't answering. Example questions show sample answers.",
                null => "Ask about the scan. Every answer names the findings it rests on."
            };
        }

        /// <summary>The list on the left and the chat on the right. The list narrows on a small window.</summary>
        private void LayoutBody()
        {
            int gutter = Px(Theme.Gutter);
            int bottom = body.ClientSize.Height - Px(Theme.Space4);
            int listWidth = body.ClientSize.Width < Px(720) ? Px(180) : Px(260);
            newChat.Bounds = new Rectangle(0, 0, listWidth, Px(Theme.ControlHeight));
            int listTop = newChat.Bottom + Px(Theme.Space3);
            listScroller.Bounds = new Rectangle(0, listTop, listWidth, Math.Max(0, bottom - listTop));
            int viewLeft = listWidth + gutter;
            view.Bounds = new Rectangle(viewLeft, 0, Math.Max(0, body.ClientSize.Width - viewLeft - Px(Theme.Space2)), Math.Max(0, bottom));
        }
    }
}
