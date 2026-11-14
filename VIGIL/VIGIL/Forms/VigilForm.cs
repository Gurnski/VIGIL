using VIGIL.Theming;

namespace VIGIL.Forms
{
    /// <summary>
    ///  Base for VIGIL's dark windows: themed colours and font, a dark title bar, and
    ///  double buffering. Inherit from this instead of Form for any new window.
    /// </summary>
    public class VigilForm : Form
    {
        public VigilForm()
        {
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            Font = Theme.Body;
            DoubleBuffered = true;
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
