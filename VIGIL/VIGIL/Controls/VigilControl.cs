using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  Base for VIGIL's custom-drawn controls: double buffered, transparent outside their
    ///  rounded corners, and themed by default.
    /// </summary>
    public abstract class VigilControl : Control
    {
        protected VigilControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor, true);

            BackColor = Color.Transparent;
            ForeColor = Theme.TextPrimary;
        }

        /// <summary>
        ///  Scales a size given in pixels at 96 DPI (100%) to this control's DPI.
        /// </summary>
        protected int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        protected float Px(float value) => value * DeviceDpi / 96f;

        /// <summary>
        ///  Bounds inset by half a pixel so 1px outlines land on whole pixels.
        /// </summary>
        protected RectangleF OutlineBounds => new(0.5f, 0.5f, Width - 1f, Height - 1f);
    }
}
