using System.ComponentModel;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  On/off switch with a sliding knob and a label to its right.
    /// </summary>
    [DefaultEvent(nameof(CheckedChanged))]
    public class ToggleSwitch : VigilControl
    {
        private readonly Transition knob;
        private bool isChecked;

        public event EventHandler? CheckedChanged;

        public ToggleSwitch()
        {
            knob = new Transition(Invalidate) { Speed = 0.3f };
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        protected override Size DefaultSize => new(200, Theme.ControlHeightSmall);

        [DefaultValue(false)]
        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value)
                {
                    return;
                }

                isChecked = value;
                if (IsHandleCreated)
                {
                    knob.To(value ? 1f : 0f);
                }
                else
                {
                    knob.Jump(value ? 1f : 0f);
                }
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            Checked = !Checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);

            float on = knob.Value;
            float trackHeight = Px(20f);
            var track = new RectangleF(0f, (float)Math.Round((Height - trackHeight) / 2f), Px(36f), trackHeight);
            var trackColour = Enabled ? Draw.Blend(Theme.SurfaceHover, Theme.Accent, on) : Theme.SurfaceRaised;
            Draw.RoundedBox(g, track, track.Height / 2f, trackColour);

            if (Focused && ShowFocusCues)
            {
                // Drawn on the track's own edge: the track starts at x = 0, so there is no room outside it.
                var ringBounds = RectangleF.Inflate(track, -0.5f, -0.5f);
                using var path = Draw.RoundedRect(ringBounds, ringBounds.Height / 2f);
                using var ring = new Pen(Draw.WithAlpha(Theme.TextPrimary, 170), 1f);
                g.DrawPath(ring, path);
            }

            float radius = Px(7f);
            float inset = Px(3f);
            float travel = track.Width - inset * 2f - radius * 2f;
            var centre = new PointF(track.X + inset + radius + travel * on, track.Y + track.Height / 2f);
            var knobColour = Enabled ? Draw.Blend(Theme.TextSecondary, Theme.TextPrimary, on) : Theme.TextDisabled;
            Draw.Circle(g, centre, radius, knobColour);

            if (Text.Length > 0)
            {
                int textLeft = (int)track.Right + Px(Theme.Space3);
                Draw.Text(g, Text, Font, new Rectangle(textLeft, 0, Width - textLeft, Height), Enabled ? Theme.TextPrimary : Theme.TextDisabled);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                knob.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
