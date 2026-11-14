using VIGIL.Controls;
using VIGIL.Theming;

namespace VIGIL.Pages
{
    /// <summary>
    ///  Stand-in for a page that hasn't been built yet: a header plus a centred icon and message.
    /// </summary>
    // Built in code with no designer file, so Visual Studio opens it as code rather than trying to load a designer.
    [System.ComponentModel.DesignerCategory("Code")]
    public class PlaceholderPage : UserControl
    {
        private readonly string glyph;

        public PlaceholderPage(string glyph, string title, string message)
        {
            this.glyph = glyph;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Background;
            Controls.Add(new PageHeader { Title = title, Subtitle = message, Dock = DockStyle.Top });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int scale(int px) => (int)Math.Round(px * DeviceDpi / 96f);
            int centreY = Height / 2;

            Draw.Text(g, glyph, Theme.Icon(36f), new Rectangle(0, centreY - scale(90), Width, scale(64)), Theme.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Draw.Text(g, "Coming soon", Theme.Title, new Rectangle(0, centreY - scale(16), Width, scale(32)), Theme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Draw.Text(g, "This page gets built over the hackathon weekend.", Theme.Body, new Rectangle(0, centreY + scale(18), Width, scale(24)), Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
