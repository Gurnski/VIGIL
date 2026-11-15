using VIGIL.Theming;

namespace VIGIL.Forms
{
    /// <summary>
    ///  Base for VIGIL's dark windows: themed colours and font, a dark title bar, and
    ///  double buffering. Inherit from this instead of Form for any new window.
    /// </summary>
    public class VigilForm : Form
    {
        // The app's icon, embedded from assets/icon/vigil.ico and shared by every window.
        private static readonly Icon? appIcon = LoadAppIcon();

        public VigilForm()
        {
            if (appIcon != null)
            {
                Icon = appIcon;
            }
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            Font = Theme.Body;
            DoubleBuffered = true;
        }

        private static Icon? LoadAppIcon()
        {
            using var stream = typeof(VigilForm).Assembly.GetManifestResourceStream("VIGIL.Assets.Icon.vigil.ico");
            return stream == null ? null : new Icon(stream);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!DesignMode)
            {
                WindowChrome.UseDarkTitleBar(this, Theme.Background);
            }
        }
    }
}
