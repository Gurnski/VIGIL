using System.ComponentModel;
using System.Drawing.Drawing2D;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  A looping loading wheel: a faint ring with a rotating arc drawn in ForeColor.
    /// </summary>
    public class SpinnerControl : VigilControl
    {
        private readonly System.Windows.Forms.Timer timer;
        private float angle;

        [DefaultValue(3f)]
        public float Thickness { get; set; } = 3f;

        public SpinnerControl()
        {
            timer = new System.Windows.Forms.Timer { Interval = 15 };
            timer.Tick += (s, e) =>
            {
                angle = (angle + 6f) % 360f;
                Invalidate();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!DesignMode)
            {
                timer.Start();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Draw.Smooth(e.Graphics);

            float inset = Thickness / 2f + 1f;
            var bounds = new RectangleF(inset, inset, Width - inset * 2f, Height - inset * 2f);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }

            using var track = new Pen(Draw.WithAlpha(ForeColor, 36), Thickness);
            using var arc = new Pen(ForeColor, Thickness)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            e.Graphics.DrawEllipse(track, bounds);
            e.Graphics.DrawArc(arc, bounds, angle, 90f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
