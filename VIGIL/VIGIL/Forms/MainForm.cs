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

        public MainForm()
        {
            InitializeComponent();

            AddPage("overview", Icons.Home, "Overview",
                () => new PlaceholderPage(Icons.Home, "Overview", "A summary of the variables you're watching will live here."));
            AddPage("variables", Icons.List, "Variables",
                () => new PlaceholderPage(Icons.List, "Variables", "Browse and pick Earth science variables to investigate."));
            AddPage("trends", Icons.Chart, "Trends",
                () => new PlaceholderPage(Icons.Chart, "Trends", "Time series, trend lines and significance tests for the chosen variables."));
            AddPage("map", Icons.Globe, "Map", () => new MapPage());
            AddPage("components", Icons.Library, "Components", () => new GalleryPage());

            navRail.SelectedChanged += NavRail_SelectedChanged;
            navRail.Select("components");
            FitToWidth();
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
        }
    }
}
