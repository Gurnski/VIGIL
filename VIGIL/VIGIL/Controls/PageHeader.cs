using System.ComponentModel;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  Page title with a one-line description underneath. Dock it to the top of each page.
    /// </summary>
    public class PageHeader : VigilControl
    {
        private const TextFormatFlags TopLeft =
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        private string title = "Page title";
        private string subtitle = "";

        protected override Size DefaultSize => Theme.Scaled(800, 72);

        [DefaultValue("Page title")]
        public string Title
        {
            get => title;
            set { title = value ?? ""; Invalidate(); }
        }

        [DefaultValue("")]
        public string Subtitle
        {
            get => subtitle;
            set { subtitle = value ?? ""; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Text(g, title, Theme.Heading, new Rectangle(0, 0, Width, Px(36)), Theme.TextPrimary, TopLeft);
            if (subtitle.Length > 0)
            {
                Draw.Text(g, subtitle, Theme.Body, new Rectangle(0, Px(38), Width, Px(20)), Theme.TextSecondary, TopLeft);
            }
        }
    }
}
