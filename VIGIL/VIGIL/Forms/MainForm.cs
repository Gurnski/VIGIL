using VIGIL.Controls;
using VIGIL.Controls.Chat;
using VIGIL.Data;
using VIGIL.Models;
using VIGIL.Pages;
using VIGIL.Theming;

namespace VIGIL.Forms
{
    /// <summary>
    ///  The app shell: navigation rail on the left, the selected page on the right.
    ///  Pages are created the first time they're opened and kept afterwards.
    /// </summary>
    public partial class MainForm : VigilForm
    {
        private readonly Dictionary<string, Func<Control>> pageFactories = new();
        private readonly Dictionary<string, Control> openedPages = new();

        // The Detective. One service holds the chats, shared by the floating panel and the Detective page.
        private readonly ChatService chats = new(new DetectiveClient());
        private readonly ChatPanel chatPanel;
        private readonly VigilButton chatLauncher = new() { Variant = ButtonVariant.Secondary, IconGlyph = Icons.Chat, Text = "Ask the Detective" };

        public MainForm()
        /// <param name="results">The results loaded at startup, shared by the pages that show them.</param>
        public MainForm(ResultsDocument results)
        {
            InitializeComponent();

            AddPage("overview", Icons.Home, "Overview",
                () => new PlaceholderPage(Icons.Home, "Overview", "A summary of the variables you're watching will live here."));
            AddPage("variables", Icons.List, "Variables",
                () => new PlaceholderPage(Icons.List, "Variables", "Browse and pick Earth science variables to investigate."));
            AddPage("trends", Icons.Chart, "Trends",
                () => new PlaceholderPage(Icons.Chart, "Trends", "Time series, trend lines and significance tests for the chosen variables."));
            AddPage("map", Icons.Globe, "Map", () =>
            {
                var map = new MapPage();
                map.FindingClicked += (s, finding) => AskAbout(finding);
                return map;
            });
            AddPage("detective", Icons.Chat, "Detective", () =>
            {
                var page = new DetectivePage(chats);
                page.CitationClicked += (s, id) => ShowFinding(id);
                return page;
            });
            AddPage("map", Icons.Globe, "Map", () => new MapPage(results));
            AddPage("components", Icons.Library, "Components", () => new GalleryPage());

            chatPanel = new ChatPanel(chats) { Visible = false };
            chatPanel.CloseRequested += (s, e) => SetChatOpen(false);
            chatPanel.ExpandRequested += (s, e) => OpenChatInPage();
            chatPanel.CitationClicked += (s, id) => ShowFinding(id);
            chatLauncher.Click += (s, e) => SetChatOpen(!chatPanel.Visible);
            Controls.Add(chatLauncher);
            Controls.Add(chatPanel);
            chatLauncher.BringToFront();
            chatPanel.BringToFront();

            navRail.SelectedChanged += NavRail_SelectedChanged;
            navRail.SizeChanged += (s, e) => PlaceChat();
            navRail.Select("components");
            FitToWidth();
        }

        /// <summary>Opens or closes the floating chat. It is never shown on the Detective page, which has the chat already.</summary>
        private void SetChatOpen(bool open)
        {
            bool onDetectivePage = navRail.SelectedKey == "detective";
            chatLauncher.Visible = !onDetectivePage;
            chatPanel.Visible = open && !onDetectivePage;
            PlaceChat();
            if (chatPanel.Visible)
            {
                chatPanel.BringToFront();
                chatPanel.FocusInput();
            }
        }

        /// <summary>Opens the floating chat with a finding attached to the next question.</summary>
        private void AskAbout(FindingSummary finding)
        {
            SetChatOpen(true);
            chatPanel.AskAbout(finding);
        }

        /// <summary>Carries the floating chat's conversation over to the Detective page.</summary>
        private void OpenChatInPage()
        {
            var conversation = chatPanel.Conversation;
            chatPanel.Visible = false;
            navRail.Select("detective");
            if (openedPages.TryGetValue("detective", out var page) && page is DetectivePage detective)
            {
                detective.Open(conversation);
            }
        }

        /// <summary>Shows a cited finding on the map.</summary>
        private void ShowFinding(string findingId)
        {
            navRail.Select("map");
            if (openedPages.TryGetValue("map", out var page) && page is MapPage map)
            {
                map.ShowFinding(findingId);
            }
        }

        /// <summary>
        ///  The launcher sits top-right of the page, level with its title, and the panel hangs
        ///  below it. Both stay inside the window however small it gets.
        /// </summary>
        private void PlaceChat()
        {
            if (chatPanel == null || navRail == null)
            {
                return;
            }

            int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);
            int margin = Px(Theme.Space6);
            int left = navRail.Right + Px(Theme.Space4);
            int right = ClientSize.Width - margin;
            int top = contentHost.Padding.Top + Px(Theme.Space1);

            // On a narrow window the launcher drops its label, so it doesn't sit on the page title.
            bool compact = ClientSize.Width < Px(TightWidth);
            string label = compact ? "" : "Ask the Detective";
            if (chatLauncher.Text != label)
            {
                chatLauncher.Text = label;
            }
            var launcher = new Size(compact ? Px(Theme.ControlHeight) : Px(172), Px(Theme.ControlHeight));
            chatLauncher.Bounds = new Rectangle(right - launcher.Width, top, launcher.Width, launcher.Height);

            int panelTop = chatLauncher.Bottom + Px(Theme.Space2);
            int width = Math.Min(Px(400), right - left);
            int height = Math.Min(Px(620), ClientSize.Height - panelTop - Px(Theme.Space4));
            chatPanel.Bounds = new Rectangle(right - width, panelTop, Math.Max(0, width), Math.Max(0, height));
        }

        // Window widths at 100% scale. Below the first the nav rail folds to icons, and below
        // the second the page margins tighten, so the page keeps its room on a small screen.
        private const int RailFoldWidth = 1180;
        private const int TightWidth = 900;
        private bool? railFolded;

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitToWidth();
        }

        /// <summary>
        ///  Fits the shell to the window width. The rail only folds or unfolds as the width
        ///  crosses the threshold, so a choice made with the Collapse button holds until then.
        /// </summary>
        private void FitToWidth()
        {
            if (navRail == null || WindowState == FormWindowState.Minimized)
            {
                return;
            }

            int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);
            bool fold = ClientSize.Width < Px(RailFoldWidth);
            if (fold != railFolded)
            {
                railFolded = fold;
                navRail.Collapsed = fold;
            }

            bool tight = ClientSize.Width < Px(TightWidth);
            var padding = tight
                ? new Padding(Px(Theme.Space4), Px(Theme.Space4), Px(Theme.Space2), 0)
                : new Padding(Px(Theme.PagePadding), Px(Theme.PagePaddingTop), Px(Theme.Space4), 0);
            if (contentHost.Padding != padding)
            {
                contentHost.Padding = padding;
            }
            PlaceChat();
        }

        private void AddPage(string key, string glyph, string title, Func<Control> factory)
        {
            pageFactories[key] = factory;
            navRail.AddItem(key, glyph, title);
        }

        private void NavRail_SelectedChanged(object? sender, EventArgs e)
        {
            string? key = navRail.SelectedKey;
            if (key == null || !pageFactories.TryGetValue(key, out var factory))
            {
                return;
            }

            if (!openedPages.TryGetValue(key, out var page))
            {
                page = factory();
                page.Dock = DockStyle.Fill;
                openedPages[key] = page;
                contentHost.Controls.Add(page);
            }

            contentHost.SuspendLayout();
            foreach (var other in openedPages.Values)
            {
                other.Visible = other == page;
            }
            contentHost.ResumeLayout();
            SetChatOpen(chatPanel.Visible);
        }
    }
}
