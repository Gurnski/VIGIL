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
        private readonly ChatService chats;
        private readonly ChatPanel chatPanel;
        private readonly VigilButton chatLauncher = new() { Variant = ButtonVariant.Secondary, IconGlyph = Icons.Chat, Text = "Ask the Detective" };

        // The zones drawn on the globe, kept between sessions, and the service that works out their trends.
        private readonly ZoneService zones = new();
        private readonly ResultsClient service;

        // Presenting: the app tells the service what is on show, and viewers opened from the service follow.
        private readonly PresenterClient presenter;
        private readonly VigilButton presentButton = new() { Variant = ButtonVariant.Ghost, IconGlyph = Icons.Share, Text = "Present" };
        private readonly PresentPanel presentPanel = new() { Visible = false };
        private readonly ToolTip presentTip = new();
        private (string? FindingId, string? Variable, Zone? Zone) onShow;
        private bool presenting;
        private bool sendingShow;
        private bool showChanged;

        // The heartbeat: every few seconds the app asks the service whether it is there, and a badge says what it heard.
        private const int HeartbeatMilliseconds = 5000;
        private readonly ServiceHealthClient health;
        private readonly Badge serviceBadge = new() { ShowDot = true, Text = "Checking the service" };
        private readonly ToolTip serviceTip = new() { AutoPopDelay = 20000 };
        private readonly System.Windows.Forms.Timer heartbeat = new() { Interval = HeartbeatMilliseconds };
        private readonly string? shownRunId;
        private bool checkingService;

        /// <param name="results">The results loaded at startup, shared by the pages that show them.</param>
        /// <param name="serviceAddress">
        ///  The analysis service that answered at startup: the one on this machine, or the public
        ///  one. Null when none did, and the app then goes on asking the usual address.
        /// </param>
        public MainForm(ResultsDocument results, string? serviceAddress = null)
        {
            // One address for everything that talks to the service, so the scan on screen, a zone's
            // trends, the Detective and the badge that says whether it is there all mean the same one.
            chats = new ChatService(new DetectiveClient(serviceAddress));
            service = new ResultsClient(serviceAddress);
            presenter = new PresenterClient(serviceAddress);
            health = new ServiceHealthClient(serviceAddress);
            InitializeComponent();
            shownRunId = results.Run?.Id;

            AddPage("overview", Icons.Home, "Overview", () =>
            {
                var overview = new OverviewPage(results);
                overview.FindingChosen += (s, id) => ShowEvidence(id);
                overview.VariableChosen += (s, variable) => ShowLayer(variable);
                overview.MapRequested += (s, e) => navRail.Select("map");
                overview.EvidenceRequested += (s, e) => navRail.Select("evidence");
                return overview;
            });
            AddPage("evidence", Icons.Chart, "Evidence", () =>
            {
                var evidence = new EvidencePage(results, service);
                evidence.ShowOnMapRequested += (s, id) => ShowFinding(id);
                evidence.AskRequested += (s, finding) => AskAbout(MapPage.Summarise(finding));
                evidence.FindingShown += (s, finding) => PutOnShow(finding.Id, finding.Variables.FirstOrDefault()?.Id, null);
                return evidence;
            });
            AddPage("map", Icons.Globe, "Map", () =>
            {
                var map = new MapPage(results, zones, service);
                map.FindingClicked += (s, finding) => AskAbout(finding);
                map.EvidenceRequested += (s, id) => ShowEvidence(id);
                map.ShowingChanged += (s, e) => PutOnShow(map.ShownFindingId, map.ShownVariable, map.ShownZone);
                return map;
            });
            AddPage("detective", Icons.Chat, "Detective", () =>
            {
                var page = new DetectivePage(chats);
                page.CitationClicked += (s, id) => ShowFinding(id);
                page.StepClicked += (s, step) => ShowStep(step);
                return page;
            });
            AddPage("components", Icons.Library, "Components", () => new GalleryPage());

            if (!results.Synthetic)
            {
                // A real scan: the fixture's sample answers are about made-up cells, so they stay out of it,
                // and the questions on offer are ones this scan can speak to. The last shows it declining.
                chats.AllowSamples = false;
                chats.Suggestions = new[]
                {
                    "Where is it warming fastest?",
                    "Where is rainfall falling fastest, and how sure is that?",
                    "What is the top-ranked finding, and how sure is it?",
                    "What will the weather be like in Plymouth tomorrow?"
                };
            }

            chatPanel = new ChatPanel(chats) { Visible = false };
            chatPanel.CloseRequested += (s, e) => SetChatOpen(false);
            chatPanel.ExpandRequested += (s, e) => OpenChatInPage();
            chatPanel.CitationClicked += (s, id) => ShowFinding(id);
            chatPanel.StepClicked += (s, step) => ShowStep(step);
            chats.Answered += Chats_Answered;
            chatLauncher.Click += (s, e) => SetChatOpen(!chatPanel.Visible);
            Controls.Add(chatLauncher);
            Controls.Add(chatPanel);
            chatLauncher.BringToFront();
            chatPanel.BringToFront();

            presentButton.Click += PresentButton_Click;
            presentPanel.StopRequested += (s, e) => StopPresenting();
            presentPanel.CloseRequested += (s, e) => presentPanel.Visible = false;
            presentTip.SetToolTip(presentButton, "Let other people's screens follow yours. Needs the analysis service.");
            Controls.Add(presentButton);
            Controls.Add(presentPanel);
            presentButton.BringToFront();
            presentPanel.BringToFront();
            FormClosing += (s, e) =>
            {
                if (presenting)
                {
                    // Nobody should be left following a presenter who has gone. One short try, then the window closes anyway.
                    try { Task.Run(() => presenter.StopAsync()).Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
                }
            };

            Controls.Add(serviceBadge);
            serviceBadge.BringToFront();
            heartbeat.Tick += async (s, e) => await CheckServiceAsync();
            Shown += async (s, e) =>
            {
                heartbeat.Start();
                await CheckServiceAsync();
            };
            FormClosed += (s, e) =>
            {
                heartbeat.Stop();
                heartbeat.Dispose();
                health.Dispose();
            };

            navRail.SelectedChanged += NavRail_SelectedChanged;
            navRail.SizeChanged += (s, e) => PlaceChat();
            // The app opens on what the scan found. The Components page is still in the rail, for whoever is building.
            navRail.Select("overview");
            FitToWidth();
        }

        /// <summary>Opens or closes the floating chat. It is never shown on the Detective page, which has the chat already.</summary>
        private void SetChatOpen(bool open)
        {
            bool onDetectivePage = navRail.SelectedKey == "detective";
            chatLauncher.Visible = !onDetectivePage;
            chatPanel.Visible = open && !onDetectivePage;
            if (chatPanel.Visible)
            {
                presentPanel.Visible = false;            // the two cards share the same corner
            }
            PlaceChat();
            if (chatPanel.Visible)
            {
                chatPanel.BringToFront();
                chatPanel.FocusInput();
            }
        }

        // ----- The service's heartbeat -----

        /// <summary>Asks the service whether it is there. One call at a time: a slow answer is not asked for twice.</summary>
        private async Task CheckServiceAsync()
        {
            if (checkingService)
            {
                return;
            }
            checkingService = true;
            try
            {
                var heard = await health.HealthAsync();
                if (!IsDisposed)
                {
                    ShowService(heard);
                }
            }
            catch (ObjectDisposedException)
            {
                // The window closed while the call was out.
            }
            finally
            {
                checkingService = false;
            }
        }

        /// <summary>
        ///  Says what the service answered, in the badge beside the Present button. The badge reports
        ///  and nothing more: each page still asks the service for itself and says so when it cannot.
        /// </summary>
        private void ShowService(ServiceHealth? heard)
        {
            string at = health.Address;
            string text, tip;
            BadgeTone tone;
            if (heard == null)
            {
                text = "Service not running";
                tone = BadgeTone.Negative;
                // The service on this machine is one the person starts. The public one is not, and may only be waking up.
                tip = "Nothing is answering at " + at + ".\nThe scan on screen still shows, but a zone's trends, Recompute, the years,\npresenting and the Detective all need the analysis service.\n"
                    + (DetectiveClient.IsLocal(at) ? "Start it with python\\start-service.cmd." : "It is asked again every few seconds, and may only be starting.");
            }
            else if (heard.RunId != shownRunId)
            {
                text = "Service holds another scan";
                tone = BadgeTone.Warning;
                tip = "The analysis service at " + at + " is running, but its scan is not the one on screen.\nClose VIGIL and open it again to load the service's scan.";
            }
            else if (!heard.ScanData)
            {
                text = "Service has no gridded data";
                tone = BadgeTone.Warning;
                tip = "The analysis service at " + at + " holds this scan but not the gridded data it was made from,\nso a zone's trends, Recompute and the years cannot be worked out.\nStart it with python\\start-service.cmd, which gives it both.";
            }
            else if (!heard.Detective)
            {
                text = "Service connected, no Detective";
                tone = BadgeTone.Warning;
                tip = "The analysis service at " + at + " is answering and holds this scan.\nIt has no key for the Detective, so questions will not be answered.\nSet ANTHROPIC_API_KEY and start the service again.";
            }
            else
            {
                text = "Service connected";
                tone = BadgeTone.Positive;
                tip = "The analysis service at " + at + " is answering, holds this scan and has a key for the Detective.";
            }

            if (serviceBadge.Text != text || serviceBadge.Tone != tone)
            {
                serviceBadge.Tone = tone;
                serviceBadge.Text = text;
                PlaceChat();
            }
            if (serviceTip.GetToolTip(serviceBadge) != tip)
            {
                serviceTip.SetToolTip(serviceBadge, tip);
            }
        }

        // ----- Presenting -----

        /// <summary>Notes what is on show. While presenting, the service is told, and the audience's screens follow.</summary>
        private void PutOnShow(string? findingId, string? variable, Zone? zone)
        {
            onShow = (findingId, variable, zone);
            if (presenting)
            {
                _ = SendShowAsync();
            }
        }

        /// <summary>
        ///  Sends what is on show, one call at a time and always ending with the latest: quick
        ///  clicks down a list must not reach the audience out of order.
        /// </summary>
        private async Task SendShowAsync()
        {
            showChanged = true;
            if (sendingShow)
            {
                return;
            }
            sendingShow = true;
            try
            {
                while (showChanged && presenting)
                {
                    showChanged = false;
                    await presenter.ShowAsync(onShow.FindingId, onShow.Variable, onShow.Zone);
                }
            }
            catch (ServiceUnavailableException error)
            {
                StopPresenting(error.Message + " Presenting has stopped.");
            }
            finally
            {
                sendingShow = false;
            }
        }

        private async void PresentButton_Click(object? sender, EventArgs e)
        {
            if (presenting)
            {
                // Already presenting: the button brings the card with the address back.
                ShowPresentPanel(!presentPanel.Visible);
                return;
            }

            presentButton.Enabled = false;
            try
            {
                var state = await presenter.ShowAsync(onShow.FindingId, onShow.Variable, onShow.Zone);
                presenting = true;
                presentButton.Variant = ButtonVariant.Secondary;
                presentPanel.SetAddresses(PresenterClient.JoinAddresses(presenter.Address), state.OpenToNetwork);
            }
            catch (ServiceUnavailableException error)
            {
                presentPanel.SetProblem(error.Message + " Presenting goes through the analysis service, so start it and try again.");
            }
            finally
            {
                presentButton.Enabled = true;
            }
            ShowPresentPanel(true);
        }

        private void ShowPresentPanel(bool show)
        {
            presentPanel.Visible = show;
            if (show)
            {
                chatPanel.Visible = false;
                presentPanel.BringToFront();
            }
            PlaceChat();
        }

        /// <summary>Stops presenting, with a reason to show when it was not the presenter's choice.</summary>
        private async void StopPresenting(string? reason = null)
        {
            bool was = presenting;
            presenting = false;
            presentButton.Variant = ButtonVariant.Ghost;
            if (reason != null)
            {
                presentPanel.SetProblem(reason);
                ShowPresentPanel(true);
            }
            else
            {
                presentPanel.Visible = false;
            }
            PlaceChat();
            if (was && reason == null)
            {
                try
                {
                    await presenter.StopAsync();
                }
                catch (ServiceUnavailableException)
                {
                    // The service has gone, so there is nobody left following.
                }
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

        /// <summary>Shows on the map where one of the Detective's lookups looked, or the findings it returned.</summary>
        private void ShowStep(AskStep step)
        {
            navRail.Select("map");
            if (openedPages.TryGetValue("map", out var page) && page is MapPage map)
            {
                map.ShowStep(step);
            }
        }

        /// <summary>
        ///  An answer has arrived. When the map is the page on screen, the findings it cites are
        ///  marked there and the globe goes to the first, so the answer and the evidence are seen
        ///  together. On any other page nothing moves: the citations are there to click.
        /// </summary>
        private void Chats_Answered(object? sender, ChatMessage answer)
        {
            if (answer.Citations.Count > 0 && navRail.SelectedKey == "map"
                && openedPages.TryGetValue("map", out var page) && page is MapPage map)
            {
                map.ShowCited(answer.Citations.Select(citation => citation.FindingId).ToList());
            }
        }

        /// <summary>Opens the map on one variable's layer.</summary>
        private void ShowLayer(string variable)
        {
            navRail.Select("map");
            if (openedPages.TryGetValue("map", out var page) && page is MapPage map)
            {
                map.ShowLayer(variable);
            }
        }

        /// <summary>Opens a finding's evidence: its series, figures, method, sources and limits.</summary>
        private void ShowEvidence(string findingId)
        {
            navRail.Select("evidence");
            if (openedPages.TryGetValue("evidence", out var page) && page is EvidencePage evidence)
            {
                evidence.ShowFinding(findingId);
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

            // The Present button sits to the launcher's left, and its card hangs where the chat does.
            string presentLabel = compact ? "" : presenting ? "Presenting" : "Present";
            if (presentButton.Text != presentLabel)
            {
                presentButton.Text = presentLabel;
            }
            int presentWidth = compact ? Px(Theme.ControlHeight) : Px(presenting ? 132 : 108);
            presentButton.Bounds = new Rectangle(chatLauncher.Left - Px(Theme.Space2) - presentWidth, top, presentWidth, launcher.Height);
            // The service's badge sits to the left of both, while there is room for it beside the page's title.
            serviceBadge.Visible = ClientSize.Width >= Px(RailFoldWidth);
            serviceBadge.Location = new Point(presentButton.Left - Px(Theme.Space3) - serviceBadge.Width, top + (launcher.Height - serviceBadge.Height) / 2);
            int cardWidth = Math.Min(Px(440), right - left);
            if (presentPanel.Width != cardWidth)
            {
                presentPanel.Width = Math.Max(0, cardWidth);
            }
            presentPanel.Location = new Point(right - presentPanel.Width, chatLauncher.Bottom + Px(Theme.Space2));

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
