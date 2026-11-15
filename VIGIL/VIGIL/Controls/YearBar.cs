using System.ComponentModel;
using VIGIL.Theming;

namespace VIGIL.Controls
{
    /// <summary>
    ///  A bar for stepping through years: a play button, the chosen year, and a track with a
    ///  mark for each year. Click or drag on the track, use the arrow keys, or press play to
    ///  run forward a year at a time. It sits over the globe, so it has an outline.
    /// </summary>
    [DefaultEvent(nameof(YearChanged))]
    public class YearBar : VigilControl
    {
        private readonly System.Windows.Forms.Timer step = new() { Interval = 650 };
        private IReadOnlyList<int> years = Array.Empty<int>();
        private int selected;
        private bool dragging;
        private bool overPlay;

        /// <summary>Raised when another year is chosen, by hand or by playing.</summary>
        public event EventHandler? YearChanged;

        public YearBar()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            step.Tick += (s, e) => Advance();
        }

        protected override Size DefaultSize => Theme.Scaled(520, 56);

        /// <summary>The years on the track, in order. The first is chosen.</summary>
        public void SetYears(IReadOnlyList<int> list)
        {
            years = list;
            selected = 0;
            Playing = false;
            Invalidate();
        }

        [Browsable(false)]
        public int SelectedIndex
        {
            get => selected;
            set
            {
                int index = years.Count == 0 ? 0 : Math.Clamp(value, 0, years.Count - 1);
                if (index == selected)
                {
                    return;
                }
                selected = index;
                Invalidate();
                YearChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>The chosen year, or null when there are none.</summary>
        [Browsable(false)]
        public int? SelectedYear => years.Count == 0 ? null : years[selected];

        /// <summary>Whether the bar is stepping forward by itself. It stops at the last year.</summary>
        [Browsable(false)]
        public bool Playing
        {
            get => step.Enabled;
            set
            {
                if (value && years.Count > 1)
                {
                    if (selected >= years.Count - 1)
                    {
                        SelectedIndex = 0;               // played to the end: start again from the first year
                    }
                    step.Start();
                }
                else
                {
                    step.Stop();
                }
                Invalidate();
            }
        }

        /// <summary>Milliseconds a year stays on show while playing.</summary>
        [DefaultValue(650)]
        public int StepMilliseconds
        {
            get => step.Interval;
            set => step.Interval = Math.Max(50, value);
        }

        private void Advance()
        {
            if (selected >= years.Count - 1)
            {
                Playing = false;
                return;
            }
            SelectedIndex = selected + 1;
            if (selected >= years.Count - 1)
            {
                Playing = false;
            }
        }

        private Rectangle PlayBounds => new(Px(Theme.Space2), (Height - Px(Theme.ControlHeightSmall)) / 2, Px(Theme.ControlHeightSmall), Px(Theme.ControlHeightSmall));

        private Rectangle YearBounds => new(PlayBounds.Right + Px(Theme.Space2), 0, Px(58), Height);

        private Rectangle TrackBounds
        {
            get
            {
                int left = YearBounds.Right + Px(Theme.Space3);
                return new Rectangle(left, Px(14), Math.Max(1, Width - left - Px(Theme.Space5)), Px(12));
            }
        }

        private float XOf(int index)
        {
            var track = TrackBounds;
            return years.Count <= 1 ? track.Left : track.Left + track.Width * index / (float)(years.Count - 1);
        }

        private int IndexAt(int x)
        {
            var track = TrackBounds;
            if (years.Count <= 1)
            {
                return 0;
            }
            return Math.Clamp((int)Math.Round((x - track.Left) * (years.Count - 1) / (double)track.Width), 0, years.Count - 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Draw.Smooth(g);
            Draw.RoundedBox(g, OutlineBounds, Px(Theme.RadiusSmall), Theme.Surface, Theme.Border);
            if (years.Count == 0)
            {
                return;
            }

            var play = PlayBounds;
            if (overPlay)
            {
                Draw.RoundedBox(g, play, Px(Theme.RadiusSmall), Theme.SurfaceHover);
            }
            Draw.Text(g, Playing ? Icons.Pause : Icons.Play, Theme.Icon(11f), play, Theme.Accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            Draw.Text(g, years[selected].ToString(System.Globalization.CultureInfo.InvariantCulture), Theme.NumericStrong, YearBounds, Theme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var track = TrackBounds;
            int middle = track.Top + track.Height / 2;
            Draw.HorizontalLine(g, track.Left, track.Right, middle, Theme.Divider);
            for (int i = 0; i < years.Count; i++)
            {
                int x = (int)Math.Round(XOf(i));
                bool passed = i <= selected;
                int half = years[i] % 5 == 0 ? Px(4) : Px(2);
                Draw.VerticalLine(g, x, middle - half, middle + half, passed ? Theme.Accent : Theme.Border);
            }
            Draw.HorizontalLine(g, track.Left, (int)Math.Round(XOf(selected)), middle, Theme.Accent);
            Draw.Circle(g, new PointF(XOf(selected), middle + 0.5f), Px(5f), Theme.Accent);

            // The first and last year under the ends, and each fifth year between, where there is room.
            int labelTop = track.Bottom + Px(4);
            int labelWidth = Px(40);
            int lastRight = int.MinValue;
            for (int i = 0; i < years.Count; i++)
            {
                bool end = i == 0 || i == years.Count - 1;
                if (!end && years[i] % 5 != 0)
                {
                    continue;
                }
                int x = (int)Math.Round(XOf(i));
                var box = new Rectangle(x - labelWidth / 2, labelTop, labelWidth, Px(14));
                if (box.Left < lastRight + Px(2) || (!end && box.Right > XOf(years.Count - 1) - labelWidth / 2 - Px(2)))
                {
                    continue;
                }
                Draw.Text(g, years[i].ToString(System.Globalization.CultureInfo.InvariantCulture), Theme.NumericSmall, box, Theme.TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                lastRight = box.Right;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left || years.Count == 0)
            {
                return;
            }
            if (PlayBounds.Contains(e.Location))
            {
                Playing = !Playing;
                return;
            }
            if (e.X >= TrackBounds.Left - Px(8))
            {
                Playing = false;
                dragging = true;
                SelectedIndex = IndexAt(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                SelectedIndex = IndexAt(e.X);
            }
            bool over = PlayBounds.Contains(e.Location);
            Cursor = over || e.X >= TrackBounds.Left - Px(8) ? Cursors.Hand : Cursors.Default;
            if (over != overPlay)
            {
                overPlay = over;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (overPlay)
            {
                overPlay = false;
                Invalidate();
            }
        }

        protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Left: Playing = false; SelectedIndex = selected - 1; break;
                case Keys.Right: Playing = false; SelectedIndex = selected + 1; break;
                case Keys.Home: Playing = false; SelectedIndex = 0; break;
                case Keys.End: Playing = false; SelectedIndex = years.Count - 1; break;
                case Keys.Space: Playing = !Playing; break;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                step.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
