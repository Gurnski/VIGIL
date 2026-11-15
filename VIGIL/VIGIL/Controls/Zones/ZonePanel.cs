using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Controls.Zones
{
    /// <summary>
    ///  Everything about zones that sits beside the globe: the button that starts the pen, a
    ///  line saying what to do, the saved zones, and under them what the analysis service
    ///  says about the chosen one. It only raises events. The page that hosts it owns the
    ///  globe, the zones and the service, and tells it what to show.
    /// </summary>
    [System.ComponentModel.DesignerCategory("Code")]
    public class ZonePanel : UserControl
    {
        private const string IdleHint = "A zone limits the findings and the layer to an area you draw on the globe.";
        private const string DrawingHint = "Hold the left button and draw round an area. Right-drag and the wheel still move the globe. Escape cancels.";

        private readonly Panel top = new() { Dock = DockStyle.Top, BackColor = Theme.Surface };
        private readonly VigilButton draw = new() { Variant = ButtonVariant.Secondary, IconGlyph = Icons.Edit, Text = "Draw a zone" };
        private readonly VigilButton clear = new() { Variant = ButtonVariant.Ghost, Text = "Whole globe", Visible = false };
        private readonly Panel scroller = new StillPanel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        private readonly ZoneList list = new();
        private readonly ZoneSummaryView summary = new() { Visible = false };
        private bool drawing;

        /// <summary>Raised by the draw button, which starts the pen or, while drawing, cancels it.</summary>
        public event EventHandler? DrawClicked;

        /// <summary>Raised by the "Whole globe" button, which lets go of the chosen zone.</summary>
        public event EventHandler? ClearClicked;

        public event EventHandler<Zone>? ZoneClicked;
        public event EventHandler<Zone>? DeleteClicked;

        /// <summary>Raised with a variable's id when its row in the chosen zone's summary is clicked.</summary>
        public event EventHandler<string>? VariableClicked;

        public ZonePanel()
        {
            BackColor = Theme.Surface;
            DoubleBuffered = true;

            draw.Click += (s, e) => DrawClicked?.Invoke(this, EventArgs.Empty);
            clear.Click += (s, e) => ClearClicked?.Invoke(this, EventArgs.Empty);
            list.ZoneClicked += (s, zone) => ZoneClicked?.Invoke(this, zone);
            list.DeleteClicked += (s, zone) => DeleteClicked?.Invoke(this, zone);

            top.Controls.Add(draw);
            top.Controls.Add(clear);
            top.Paint += Top_Paint;
            top.Resize += (s, e) => ArrangeTop();

            summary.VariableClicked += (s, variable) => VariableClicked?.Invoke(this, variable);
            summary.SizeChanged += (s, e) => ArrangeBody();
            list.SizeChanged += (s, e) => ArrangeBody();
            scroller.Controls.Add(list);
            scroller.Controls.Add(summary);
            scroller.ClientSizeChanged += (s, e) => ArrangeBody();

            // The fill control goes in first; the one docked after it takes its edge first.
            Controls.Add(scroller);
            Controls.Add(top);
            ArrangeTop();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowChrome.UseDarkScrollbars(scroller);
            ArrangeBody();
        }

        /// <summary>Whether the pen is in use. Changes the button and the line under it.</summary>
        public bool Drawing
        {
            get => drawing;
            set
            {
                drawing = value;
                draw.Text = value ? "Cancel drawing" : "Draw a zone";
                draw.IconGlyph = value ? Icons.Cancel : Icons.Edit;
                top.Invalidate();
            }
        }

        /// <summary>Lists the saved zones and marks the chosen one, if any.</summary>
        public void SetZones(IReadOnlyList<ZoneListItem> zones, string? selectedId)
        {
            list.SetItems(zones, selectedId);
            clear.Visible = selectedId != null;
            if (selectedId == null)
            {
                summary.Visible = false;
            }
            ArrangeBody();
        }

        /// <summary>The variable marked and charted in the chosen zone's summary. The page keeps it in step with the globe's layer.</summary>
        public string? SelectedVariable
        {
            get => summary.SelectedVariable;
            set => summary.SelectedVariable = value;
        }

        /// <summary>Under the list, a line about the chosen zone: that its figures are on their way, or why there are none.</summary>
        public void ShowZoneMessage(string zoneName, string text)
        {
            summary.ShowMessage(zoneName, text);
            summary.Visible = true;
            ArrangeBody();
        }

        /// <summary>Under the list, the service's figures for the chosen zone.</summary>
        public void ShowZoneAnalysis(string zoneName, ZoneAnalysis analysis)
        {
            summary.ShowAnalysis(zoneName, analysis);
            summary.Visible = true;
            ArrangeBody();
        }

        /// <summary>The list, then the summary under it, both as wide as the scrolling panel.</summary>
        private void ArrangeBody()
        {
            int width = scroller.ClientSize.Width;
            var scrolled = scroller.AutoScrollPosition;
            if (list.Width != width || list.Location != scrolled)
            {
                list.SetBounds(scrolled.X, scrolled.Y, width, list.Height);
            }
            var below = new Point(scrolled.X, scrolled.Y + list.Height + Px(Theme.Space3));
            if (summary.Width != width || summary.Location != below)
            {
                summary.SetBounds(below.X, below.Y, width, summary.Height);
            }
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        private Rectangle HintBounds => new(0, Px(Theme.ControlHeight + Theme.Space2), top.Width, Px(48));

        private void ArrangeTop()
        {
            int height = Px(Theme.ControlHeight);
            draw.SetBounds(0, 0, Px(156), height);
            clear.SetBounds(draw.Right + Px(Theme.Space2), 0, Px(112), height);
            top.Height = HintBounds.Bottom + Px(Theme.Space2);
            top.Invalidate();
        }

        private void Top_Paint(object? sender, PaintEventArgs e)
        {
            Draw.Text(e.Graphics, drawing ? DrawingHint : IdleHint, Theme.Caption, HintBounds, drawing ? Theme.TextPrimary : Theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }
}
