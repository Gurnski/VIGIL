using System.Globalization;
using VIGIL.Controls;
using VIGIL.Controls.Globe;
using VIGIL.Controls.Zones;
using VIGIL.Data;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Pages
{
    /// <summary>
    ///  The Map page: a 3D globe carrying one variable's trend per cell, beside the ranked list
    ///  of findings. Choosing a finding, in the list or by its marker, switches to its variable,
    ///  outlines its cell and flies there. A toolbar searches for places, jumps to presets and
    ///  switches layers. The results come from the document handed in at startup.
    ///
    ///  The Zones tab beside the findings starts a pen for drawing an area on the globe. A
    ///  saved zone, once chosen, limits the findings, their markers and the layer to the grid
    ///  cells whose centres it holds.
    /// </summary>
    // Built in code with no designer file, so Visual Studio opens it as code rather than trying to load a designer.
    [System.ComponentModel.DesignerCategory("Code")]
    public class MapPage : UserControl
    {
        private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");
        private static readonly string[] PresetNames = { "Amazon", "Greenland", "Sahel", "Antarctica" };

        private const float LayerOpacity = 0.7f;

        // A marker per cell for the top of the list only: a real scan has thousands of findings,
        // and that many dots would hide the layer they sit on.
        private const int MaxMarkedFindings = 200;

        // Of those, the cells whose marker carries its rank beside it all the time. The rest show
        // theirs under the mouse: two hundred labels would sit on each other and on the layer.
        private const int LabelledMarkers = 24;

        private readonly ResultsDocument results;
        private readonly ZoneService zones;
        private readonly ResultsClient service;
        private readonly Dictionary<string, GlobeMarker> markersByFinding = new();

        // The globe opens still. Turning it by itself keeps a processor core busy, so that is for whoever switches it on.
        private readonly GlobeControl globe = new() { Dock = DockStyle.Fill, AutoRotate = false };
        private readonly SearchBox search = new() { Placeholder = "Fly to a place…" };
        private readonly SegmentedControl layers = new();
        private readonly SegmentedControl presets = new();
        private readonly ToggleSwitch graticuleToggle = new() { Text = "Grid lines" };
        private readonly ToggleSwitch rotateToggle = new() { Text = "Rotate", Checked = false };
        private readonly ToggleSwitch overlayToggle = new() { Text = "Layer", Checked = true };
        private readonly ToggleSwitch significantToggle = new() { Text = "Significant only" };
        private readonly ToggleSwitch reliefToggle = new() { Text = "Relief" };
        private readonly ToggleSwitch flatToggle = new() { Text = "Flat map" };

        // The layer year by year: the service's annual anomalies for the chosen variable, kept once fetched.
        private readonly ToggleSwitch yearsToggle = new() { Text = "Years" };
        private readonly YearBar yearBar = new() { Visible = false };
        private readonly Dictionary<string, YearsField> yearsByVariable = new();
        private string? yearsAsked;
        private readonly Badge status = new() { Text = "", Tone = BadgeTone.Warning };
        private readonly ColourScaleLegend legend = new();
        private readonly Panel findingsHost = new();
        private readonly Card findingsCard = new() { Title = "Findings", Dock = DockStyle.Fill };
        private readonly FindingsList findingsList = new() { Dock = DockStyle.Fill };
        private readonly Badge syntheticBadge = new() { Text = "Synthetic fixture", Tone = BadgeTone.Warning };
        private readonly ToggleSwitch satellitesToggle = new() { Text = "Satellites", Checked = true };
        private readonly ToolTip satellitesTip = new() { InitialDelay = 400 };
        private readonly SatelliteCard satelliteCard = new() { Visible = false };

        // The Findings and Zones tabs share the column beside the globe.
        private readonly SegmentedControl sideTabs = new();
        private readonly Panel sideTabsHost = new() { Dock = DockStyle.Top, BackColor = Theme.Background };
        private readonly Card zonesCard = new() { Title = "Zones", Dock = DockStyle.Fill, Visible = false };
        private readonly ZonePanel zonePanel = new() { Dock = DockStyle.Fill };
        private readonly ZoneNameBar zoneNameBar = new() { Visible = false };
        private readonly Panel scanLine = new() { Dock = DockStyle.Top, BackColor = Theme.Surface };
        private readonly Panel evidenceHost = new() { Dock = DockStyle.Bottom, BackColor = Theme.Surface };
        private readonly VigilButton evidenceButton = new() { Variant = ButtonVariant.Secondary, IconGlyph = Icons.Chart, Text = "Open the evidence", Enabled = false };

        private Place? flightDestination;
        private bool? findingsBelow;

        // The chosen zone and the grid cells it takes in, as rows and columns of the scan grid.
        private Zone? activeZone;
        private HashSet<(int Row, int Column)> activeCells = new();
        private readonly Dictionary<string, List<(int Row, int Column)>> cellsByZone = new();
        private IReadOnlyList<(double Latitude, double Longitude)>? pendingOutline;
        private string scanText = "";
        // The service's figures for each zone asked about this session. A zone's outline never changes, so they keep.
        private readonly Dictionary<string, ZoneAnalysis> analysisByZone = new();

        /// <summary>Raised when a finding's marker is clicked, so the Detective can be asked about it.</summary>
        public event EventHandler<FindingSummary>? FindingClicked;

        /// <summary>Raised with a finding's id by "Open the evidence".</summary>
        public event EventHandler<string>? EvidenceRequested;

        /// <summary>Raised when the finding, the layer or the zone on show changes, for a presenter's audience to follow.</summary>
        public event EventHandler? ShowingChanged;

        /// <summary>The finding chosen in the list, if any.</summary>
        public string? ShownFindingId => findingsList.SelectedFinding?.Id;

        /// <summary>The variable whose layer is on the globe, or null when the layer is switched off.</summary>
        public string? ShownVariable =>
            overlayToggle.Checked && layers.SelectedIndex >= 0 && layers.SelectedIndex < results.Grids.Count
                ? results.Grids[layers.SelectedIndex].Variable
                : null;

        /// <summary>The zone chosen, if any.</summary>
        public Zone? ShownZone => activeZone;

        public MapPage(ResultsDocument results, ZoneService zones, ResultsClient service)
        {
            this.results = results;
            this.zones = zones;
            this.service = service;
            BackColor = Theme.Background;
            DoubleBuffered = true;

            // The fill control goes in first. Those docked after it take their edges first: the
            // header, then the toolbar, then the findings, and the globe has what is left.
            Controls.Add(globe);
            Controls.Add(BuildFindings());
            Controls.Add(BuildToolbar());
            Controls.Add(new PageHeader
            {
                Title = "Map",
                Subtitle = "Drag to rotate, scroll to zoom, right-drag to tilt. Click a finding to ask the Detective about it.",
                Dock = DockStyle.Top,
                Height = Px(72)
            });

            legend.Size = new Size(Px(290), Px(68));
            legend.Follow(globe);
            globe.Controls.Add(legend);
            globe.Controls.Add(yearBar);
            globe.Controls.Add(status);
            satelliteCard.Width = Px(300);
            satelliteCard.CloseRequested += (s, e) => satelliteCard.Visible = false;
            globe.Controls.Add(satelliteCard);
            globe.SatelliteClicked += Globe_SatelliteClicked;

            globe.MarkerClicked += Globe_MarkerClicked;
            globe.RegionSelected += Globe_RegionSelected;
            globe.FlyToComplete += Globe_FlyToComplete;
            // Any hand on the globe cancels a flight without FlyToComplete, so forget where it was going.
            globe.MouseDown += (s, e) => flightDestination = null;
            globe.MouseWheel += (s, e) => flightDestination = null;
            globe.KeyDown += (s, e) => flightDestination = null;
            globe.Resize += (s, e) => PositionOverlays();

            zoneNameBar.Width = Px(320);
            zoneNameBar.SaveRequested += (s, e) => SavePendingZone();
            zoneNameBar.DiscardRequested += (s, e) => globe.ClearStroke();
            globe.Controls.Add(zoneNameBar);
            globe.ZoneDrawn += Globe_ZoneDrawn;
            globe.ZoneClicked += Globe_ZoneClicked;
            globe.PenModeChanged += (s, e) => zonePanel.Drawing = globe.PenMode;
            globe.StrokeCleared += (s, e) =>
            {
                pendingOutline = null;
                zoneNameBar.Visible = false;
            };
            zonePanel.DrawClicked += ZonePanel_DrawClicked;
            zonePanel.ClearClicked += (s, e) => ChooseZone(null, false);
            zonePanel.ZoneClicked += (s, zone) => ChooseZone(activeZone?.Id == zone.Id ? null : zone, true);
            zonePanel.DeleteClicked += (s, zone) => zones.Delete(zone);
            zonePanel.VariableClicked += (s, variable) =>
            {
                int layer = results.Grids.FindIndex(grid => grid.Variable == variable);
                if (layer >= 0)
                {
                    layers.SelectedIndex = layer;
                }
            };
            zones.ListChanged += Zones_ListChanged;

            ShowSatellites(true);
            ShowZones();
            ArrangeFindings();
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        /// <summary>
        ///  A variable's name on its layer tab. Every tab is as wide as the widest, so the one long
        ///  name is shortened here. The legend and the findings still give it in full.
        /// </summary>
        private static string LayerLabel(string variable) =>
            variable == "sea_surface_temperature" ? "Sea temperature" : FindingsList.LabelText(variable);

        private Control BuildToolbar()
        {
            // Wraps onto a second row when the window is narrow; AutoSize lets the height follow.
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Theme.Background,
                WrapContents = true,
                Padding = new Padding(0, 0, 0, Px(Theme.Space3))
            };

            search.Width = Px(190);
            search.Margin = new Padding(0, 0, Px(Theme.Space3), Px(Theme.Space2));
            search.TextChanged += Search_TextChanged;
            var textBox = search.Controls.OfType<TextBox>().FirstOrDefault();
            if (textBox != null)
            {
                textBox.KeyDown += Search_KeyDown;
            }
            bar.Controls.Add(search);

            // One segment per globe layer in the results, named from the variable's id.
            layers.Font = Theme.Caption;
            layers.SetItems(results.Grids.Select(layer => LayerLabel(layer.Variable)).ToArray());
            layers.Height = Px(Theme.ControlHeight);
            layers.Margin = new Padding(0, 0, Px(Theme.Space3), Px(Theme.Space2));
            layers.Visible = results.Grids.Count > 0;
            layers.SelectedChanged += (s, e) => ShowSelectedLayer();
            bar.Controls.Add(layers);

            presets.Font = Theme.Caption;
            presets.SetItems(PresetNames);
            presets.SelectedIndex = -1;
            presets.Height = Px(Theme.ControlHeight);
            presets.Margin = new Padding(0, 0, Px(Theme.Space3), Px(Theme.Space2));
            presets.SelectedChanged += Presets_SelectedChanged;
            bar.Controls.Add(presets);

            // The switches sit in a group of their own, so they wrap to the next row together
            // and one is never left behind on a row by itself.
            var switches = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                BackColor = Theme.Background,
                Margin = Padding.Empty
            };
            int toggleTop = Px((Theme.ControlHeight - Theme.ControlHeightSmall) / 2);
            foreach (var toggle in new[] { overlayToggle, significantToggle, reliefToggle, flatToggle, yearsToggle, satellitesToggle, graticuleToggle, rotateToggle })
            {
                // Track (36) plus gap (12) plus the label, so each toggle takes only the room it needs.
                toggle.Font = Theme.Body;
                toggle.Width = TextRenderer.MeasureText(toggle.Text, Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width + Px(36 + Theme.Space3 + Theme.Space2);
                toggle.Margin = new Padding(Px(Theme.Space2), toggleTop, Px(Theme.Space2), Px(Theme.Space2));
                switches.Controls.Add(toggle);
            }
            bar.Controls.Add(switches);
            graticuleToggle.CheckedChanged += (s, e) => globe.ShowGraticule = graticuleToggle.Checked;
            rotateToggle.CheckedChanged += (s, e) => globe.AutoRotate = rotateToggle.Checked;
            overlayToggle.CheckedChanged += (s, e) => globe.GridOverlayOpacity = overlayToggle.Checked ? LayerOpacity : 0f;
            overlayToggle.CheckedChanged += (s, e) => ShowingChanged?.Invoke(this, EventArgs.Empty);
            layers.SelectedChanged += (s, e) => ShowingChanged?.Invoke(this, EventArgs.Empty);
            significantToggle.CheckedChanged += (s, e) => ShowSelectedLayer();
            reliefToggle.CheckedChanged += (s, e) => ShowRelief(reliefToggle.Checked);
            flatToggle.CheckedChanged += (s, e) => ShowFlat(flatToggle.Checked);
            yearsToggle.CheckedChanged += (s, e) => ShowYears(yearsToggle.Checked);
            yearBar.YearChanged += (s, e) => ShowSelectedLayer();
            satellitesTip.SetToolTip(yearsToggle, "Shows each year against the cell's own normal, in place of the trend. Press play to watch a change arrive. Needs the analysis service.");
            satellitesTip.SetToolTip(reliefToggle, "Raises each cell by the size of its trend. The colour still says which way it goes.");
            // A switch that is out of use shows no tip, so what the flat map does to "Satellites" and "Rotate" is said here.
            satellitesTip.SetToolTip(flatToggle, "Unrolls the globe into a flat map. Drag the map to move it, and scroll to zoom.\nThe satellites are not shown on it: their orbits are drawn round the globe. A map does not rotate.");
            satellitesToggle.CheckedChanged += (s, e) => ShowSatellites(satellitesToggle.Checked);
            satellitesTip.SetToolTip(satellitesToggle, MissionSatellites.Caveat);

            return bar;
        }

        /// <summary>
        ///  The column beside the globe: two tabs, Findings and Zones. The findings card says
        ///  what the scan did in a line, then lists the findings in rank order.
        /// </summary>
        private Control BuildFindings()
        {
            findingsHost.BackColor = Theme.Background;
            // The fill controls go in first; the tabs are docked after them and take the top edge.
            findingsHost.Controls.Add(findingsCard);
            findingsHost.Controls.Add(zonesCard);
            zonesCard.Controls.Add(zonePanel);

            sideTabs.Font = Theme.Caption;
            sideTabs.SetItems("Findings", "Zones");
            sideTabs.Height = Px(32);
            sideTabs.SelectedChanged += (s, e) =>
            {
                findingsCard.Visible = sideTabs.SelectedIndex != 1;
                zonesCard.Visible = sideTabs.SelectedIndex == 1;
            };
            sideTabsHost.Height = sideTabs.Height + Px(Theme.Space3);
            sideTabsHost.Controls.Add(sideTabs);
            findingsHost.Controls.Add(sideTabsHost);

            findingsList.SetFindings(results.Findings);
            findingsList.SelectedChanged += FindingsList_SelectedChanged;
            findingsCard.Controls.Add(findingsList);

            // Drawn by hand: a Label insets its text by a few pixels, which would put it out of line with the card title.
            scanLine.Height = Px(40);
            scanLine.Paint += (s, e) => Draw.Text(e.Graphics, scanText, Theme.Caption, scanLine.ClientRectangle, Theme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            scanLine.Resize += (s, e) => scanLine.Invalidate();
            findingsCard.Controls.Add(scanLine);

            // Under the list: the way from a finding on the map to everything the results say about it.
            evidenceHost.Height = Px(Theme.ControlHeight + Theme.Space3);
            evidenceButton.SetBounds(0, Px(Theme.Space3), Px(196), Px(Theme.ControlHeight));
            evidenceButton.Click += (s, e) =>
            {
                if (findingsList.SelectedFinding is Finding chosen)
                {
                    EvidenceRequested?.Invoke(this, chosen.Id);
                }
            };
            evidenceHost.Controls.Add(evidenceButton);
            findingsCard.Controls.Add(evidenceHost);

            // Made-up numbers are labelled wherever they are listed.
            syntheticBadge.Visible = results.Synthetic;
            findingsCard.Controls.Add(syntheticBadge);
            syntheticBadge.BringToFront();
            findingsCard.Resize += (s, e) => PositionSyntheticBadge();
            return findingsHost;
        }

        private void PositionSyntheticBadge()
        {
            var titleLine = findingsCard.TitleBounds;
            syntheticBadge.Location = new Point(titleLine.Right - syntheticBadge.Width, titleLine.Y + (titleLine.Height - syntheticBadge.Height) / 2);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ArrangeFindings();
        }

        /// <summary>Findings sit to the right of the globe, or underneath it when the page is too narrow for both.</summary>
        private void ArrangeFindings()
        {
            bool below = Width < Px(860);
            if (below == findingsBelow)
            {
                return;
            }
            findingsBelow = below;

            int gap = Px(Theme.Gutter);
            int bottom = Px(Theme.Space4);
            findingsHost.Dock = below ? DockStyle.Bottom : DockStyle.Right;
            findingsHost.Padding = below ? new Padding(0, gap, 0, bottom) : new Padding(gap, 0, 0, bottom);
            findingsHost.Size = below ? new Size(Width, Px(264)) : new Size(Px(380), Height);
            PositionSyntheticBadge();
        }

        /// <summary>
        ///  Draws the layer as columns, and says on the legend what their height means. The legend
        ///  grows by a line to hold it.
        /// </summary>
        private void ShowRelief(bool on)
        {
            globe.Relief = on;
            if (on && globe.Tilt < 15)
            {
                // Seen from straight above a column is only its lid, so the view leans over to show its height.
                globe.TiltTo(50);
            }
            NoteOnLegend();
            PositionOverlays();
        }

        /// <summary>
        ///  Unrolls the globe into a flat map, or rolls it back up. The satellites are drawn round
        ///  the globe only, so their switch is out of use while the map is flat, and a map does not
        ///  turn, so neither is "Rotate". The "Flat map" switch's own tip says so, because a switch
        ///  that is out of use shows none.
        /// </summary>
        private void ShowFlat(bool on)
        {
            globe.Flat = on;
            satellitesToggle.Enabled = !on;
            rotateToggle.Enabled = !on;
            if (on)
            {
                satelliteCard.Visible = false;
            }
        }

        /// <summary>
        ///  Says under the legend's scale what the scale does not: that the years are measured
        ///  against each cell's own normal, and what a column's height stands for. The legend
        ///  grows by a line to hold it.
        /// </summary>
        private void NoteOnLegend()
        {
            bool years = yearsToggle.Checked && yearBar.Visible;
            bool relief = reliefToggle.Checked;
            legend.Note = years && relief ? "Against its normal. Height: how far from it."
                : years ? "Each year against the cell's own normal"
                : relief ? "Height: the size of the trend, either way"
                : "";
            legend.Height = Px(legend.Note.Length > 0 ? 86 : 68);
        }

        /// <summary>Legend top-left and the status badge top-right; the globe's own HUD has the bottom corners.</summary>
        private void PositionOverlays()
        {
            int edge = Px(Theme.Space4);
            legend.Location = new Point(edge, edge);
            // The layer needs its key, so the legend stays until the globe is too small to hold it at all.
            legend.Visible = globe.GridOverlayScale != null && globe.Width >= Px(420) && globe.Height >= Px(320);
            int statusLeft = globe.Width - edge - status.Width;
            bool clear = !legend.Visible || statusLeft >= legend.Right + edge;
            // The satellite card sits under the legend when there is room for all of it. On a short globe
            // it moves up to fit, covering the legend while it is open rather than running off the bottom.
            int underLegend = legend.Visible ? legend.Bottom + Px(Theme.Space2) : edge;
            bool fits = underLegend + satelliteCard.Height + edge <= globe.Height;
            satelliteCard.Location = new Point(edge, fits ? underLegend : Math.Max(edge, globe.Height - satelliteCard.Height - edge));
            // The name for a new zone goes top centre, or under the legend when the globe is too narrow to clear it.
            int nameLeft = Math.Max(edge, (globe.Width - zoneNameBar.Width) / 2);
            zoneNameBar.Location = new Point(nameLeft, legend.Visible && nameLeft < legend.Right + edge ? underLegend : edge);
            status.Location = clear ? new Point(Math.Max(edge, statusLeft), edge) : new Point(edge, legend.Bottom + Px(Theme.Space2));
            // The years bar goes bottom centre, clear of the read-out on its left and the zoom buttons on its right.
            // On a narrow globe there is no room between them, so it sits above them.
            // There it also stops short of the zoom buttons, which run up the right-hand edge.
            bool wide = globe.Width >= Px(1040);
            int buttons = Px(56);
            yearBar.Width = Math.Min(Px(520), Math.Max(Px(240), globe.Width - edge * 2 - (wide ? 0 : buttons)));
            yearBar.Location = wide
                ? new Point((globe.Width - yearBar.Width) / 2, globe.Height - yearBar.Height - Px(36))
                : new Point(edge, globe.Height - yearBar.Height - Px(120));
        }

        private void SetStatus(string text)
        {
            status.Text = text;
            PositionOverlays();
        }

        /// <summary>
        ///  Paints the selected variable's layer: Sen's slope per decade in each scanned cell,
        ///  exactly as the service reported it, with skipped cells left clear. Cells are drawn
        ///  as cells, not blended, because each one is a separate test.
        /// </summary>
        private void ShowSelectedLayer()
        {
            int index = layers.SelectedIndex;
            if (index < 0 || index >= results.Grids.Count)
            {
                globe.ClearGridOverlay();
                PositionOverlays();
                return;
            }

            var layer = results.Grids[index];
            // The chosen zone's summary follows the layer: one variable at a time, on the globe and beside it.
            zonePanel.SelectedVariable = layer.Variable;
            if (yearsToggle.Checked && ShowYearOf(layer))
            {
                return;
            }
            double threshold = results.Run?.Settings.Threshold ?? 0.05;
            bool significantOnly = significantToggle.Checked;
            var values = new double[Math.Max(1, layer.Rows), Math.Max(1, layer.Columns)];
            var magnitudes = new List<double>();
            for (int row = 0; row < values.GetLength(0); row++)
            {
                for (int column = 0; column < values.GetLength(1); column++)
                {
                    double? slope = At(layer.SlopePerDecade, row, column);
                    double? adjusted = At(layer.PAdjusted, row, column);
                    if (slope is double value)
                    {
                        magnitudes.Add(Math.Abs(value));
                    }
                    // A chosen zone leaves the cells outside it clear. The scale above still comes from every cell.
                    bool shown = slope != null && (!significantOnly || adjusted < threshold) && InActiveZone(layer, row, column);
                    values[row, column] = shown ? slope!.Value : double.NaN;
                }
            }

            // The scale is centred on no change and set from every scanned cell, so it stays put
            // when "Significant only" is switched. It ends at the 98th percentile: one extreme
            // cell would otherwise wash out the rest. Cells beyond the ends take the end colour.
            double limit = ScaleLimit(magnitudes);
            legend.Title = FindingsList.LabelText(layer.Variable) + " trend, " + layer.Units;
            legend.ValueFormat = limit >= 10 ? "0" : limit >= 1 ? "0.0" : limit >= 0.1 ? "0.00" : "0.000";
            globe.SetGridOverlay(values, layer.LatMin, layer.LatMax, layer.LonMin, layer.LonMax,
                ColourScale.BlueWhiteRed, -limit, limit, overlayToggle.Checked ? LayerOpacity : 0f);
            PositionOverlays();
        }

        // ----- The layer year by year -----

        /// <summary>Switches between the trend and the years. The trend comes straight back when the years are switched off.</summary>
        private void ShowYears(bool on)
        {
            significantToggle.Enabled = !on;             // a year's anomaly has no significance to filter by
            if (!on)
            {
                yearBar.Playing = false;
                yearBar.Visible = false;
                SetStatus("");
                NoteOnLegend();
            }
            ShowSelectedLayer();
        }

        /// <summary>
        ///  Paints the chosen year of the layer's variable, as the service gave it. Returns false
        ///  while the service's answer is not here yet, so the trend stays on the globe meanwhile.
        /// </summary>
        private bool ShowYearOf(GridLayer layer)
        {
            if (!yearsByVariable.TryGetValue(layer.Variable, out var field))
            {
                FetchYears(layer.Variable);
                return false;
            }
            if (!ReferenceEquals(yearBar.Tag, field))
            {
                // Another variable: the bar keeps its year when the new variable has the same years.
                int kept = yearBar.SelectedIndex;
                bool sameYears = yearBar.Tag is YearsField before && before.Years.SequenceEqual(field.Years);
                yearBar.Tag = field;
                yearBar.SetYears(field.Years);
                if (sameYears)
                {
                    yearBar.SelectedIndex = kept;
                }
            }
            yearBar.Visible = true;
            NoteOnLegend();

            int year = yearBar.SelectedIndex;
            var values = new double[Math.Max(1, field.Rows), Math.Max(1, field.Columns)];
            for (int row = 0; row < values.GetLength(0); row++)
            {
                for (int column = 0; column < values.GetLength(1); column++)
                {
                    double? value = year < field.Values.Length ? At(field.Values[year], row, column) : null;
                    values[row, column] = value != null && InActiveZone(layer, row, column) ? value.Value : double.NaN;
                }
            }

            // One scale for every year, from the service, so a colour means the same in 2003 as in 2023.
            double limit = RoundedUp(field.ScaleMax ?? 1);
            // The short name, so the year and the units fit on the legend's one line. The note under the scale says "against its normal".
            legend.Title = LayerLabel(layer.Variable) + " in " + yearBar.SelectedYear + ", " + field.Units;
            legend.ValueFormat = limit >= 10 ? "0" : limit >= 1 ? "0.0" : limit >= 0.1 ? "0.00" : "0.000";
            globe.SetGridOverlay(values, field.LatMin, field.LatMax, field.LonMin, field.LonMax,
                ColourScale.BlueWhiteRed, -limit, limit, overlayToggle.Checked ? LayerOpacity : 0f);
            PositionOverlays();
            return true;
        }

        private async void FetchYears(string variable)
        {
            if (yearsAsked == variable)
            {
                return;                                  // already on its way
            }
            yearsAsked = variable;
            SetStatus("Asking the analysis service for " + FindingsList.LabelText(variable).ToLowerInvariant() + " year by year…");
            try
            {
                var field = await service.YearsAsync(variable);
                if (field.RunId != results.Run?.Id)
                {
                    throw new ServiceUnavailableException("The analysis service holds a different scan from the one on screen, so its years are not shown. "
                        + "Start the app again while the service is running to load the service's scan.");
                }
                yearsByVariable[variable] = field;
                SetStatus("");
            }
            catch (Exception error)
            {
                yearsAsked = null;
                yearsToggle.Checked = false;             // which clears the status, so the reason goes up after it
                SetStatus(error is ServiceUnavailableException ? "Years: " + error.Message : "Years: something went wrong asking the service.");
                return;
            }
            yearsAsked = null;
            if (yearsToggle.Checked)
            {
                ShowSelectedLayer();
            }
        }

        private static double? At(double?[][] cells, int row, int column) =>
            row < cells.Length && column < cells[row].Length ? cells[row][column] : null;

        /// <summary>The 98th percentile of the magnitudes, rounded up to two significant figures.</summary>
        private static double ScaleLimit(List<double> magnitudes)
        {
            if (magnitudes.Count == 0)
            {
                return 1;
            }
            magnitudes.Sort();
            double limit = magnitudes[(int)(0.98 * (magnitudes.Count - 1))];
            if (limit <= 0)
            {
                limit = magnitudes[^1];
            }
            if (limit <= 0)
            {
                return 1;
            }
            return RoundedUp(limit);
        }

        /// <summary>A scale's end rounded up to two significant figures, so the legend's labels are short.</summary>
        private static double RoundedUp(double limit)
        {
            if (limit <= 0)
            {
                return 1;
            }
            double unit = Math.Pow(10, Math.Floor(Math.Log10(limit)) - 1);
            return Math.Ceiling(limit / unit - 1e-9) * unit;
        }

        /// <summary>
        ///  One marker per cell that has a finding, labelled with the ranks found there and
        ///  carrying the top-ranked one. Several findings often share a cell.
        /// </summary>
        private void AddFindingMarkers(IReadOnlyList<Finding> findings)
        {
            foreach (var cell in findings.Take(MaxMarkedFindings).GroupBy(finding => finding.Cell.Id))
            {
                var here = cell.ToList();
                var top = here[0];
                string label = "#" + top.Rank.ToString(British) + (here.Count > 1 ? " and " + (here.Count - 1).ToString(British) + " more" : "");
                // Accent because a marker can be acted on; the quieter colour where nothing in the cell is significant.
                var colour = here.Any(finding => finding.Significant) ? Theme.Accent : Theme.TextSecondary;
                var marker = globe.AddMarker(top.Cell.CentreLatitude, top.Cell.CentreLongitude, label, colour, top.Summary);
                marker.Tag = top;
                marker.ShowLabel = markersByFinding.Values.Distinct().Count() < LabelledMarkers;
                foreach (var finding in here)
                {
                    markersByFinding[finding.Id] = marker;
                }
            }
        }

        /// <summary>
        ///  Shows or hides the satellites behind the data, each on its own orbit. They are there
        ///  to show where the measurements come from: no result depends on them.
        /// </summary>
        private void ShowSatellites(bool show)
        {
            globe.ClearSatellites();
            satelliteCard.Visible = false;
            if (show)
            {
                var inScan = results.Variables.Select(variable => variable.Mission).ToList();
                var reTests = results.Run?.Retest?.ByVariable.Select(entry => entry.Product.Mission).ToList() ?? new List<string>();
                foreach (var satellite in MissionSatellites.Create(inScan, reTests))
                {
                    globe.AddSatellite(satellite);
                }
            }
        }

        /// <summary>Switches to the finding's variable, outlines its cell (and the paired cell of a contradiction) and flies there.</summary>
        private void ShowFinding(Finding finding)
        {
            flightDestination = null;
            presets.SelectedIndex = -1;

            string? variable = finding.Variables.FirstOrDefault()?.Id;
            int layer = results.Grids.FindIndex(grid => grid.Variable == variable);
            if (layer >= 0)
            {
                layers.SelectedIndex = layer;
            }

            globe.ClearRegions();
            OutlineCell(finding.Cell);
            if (finding.Contradiction?.PairedCell is Cell paired)
            {
                OutlineCell(paired);
            }

            markersByFinding.TryGetValue(finding.Id, out var marker);
            globe.SelectMarker(marker);
            globe.FlyTo(finding.Cell.CentreLatitude, finding.Cell.CentreLongitude, Math.Max(globe.Zoom, 3));
            SetStatus("#" + finding.Rank.ToString(British) + " " + FindingsList.LabelText(finding.Kind) + ", " + FindingsList.PlaceText(finding.Cell));
        }

        private void OutlineCell(Cell cell)
        {
            // No fill, so the layer's colour in the cell stays readable.
            globe.AddRegion(cell.LatMin, cell.LatMax, cell.LonMin, cell.LonMax, Theme.Accent, "", 0f);
        }

        private void FindingsList_SelectedChanged(object? sender, EventArgs e)
        {
            evidenceButton.Enabled = findingsList.SelectedFinding != null;
            if (findingsList.SelectedFinding is Finding finding)
            {
                ShowFinding(finding);
            }
            ShowingChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Search_TextChanged(object? sender, EventArgs e)
        {
            var place = Places.Find(search.Text);
            if (search.Text.Trim().Length == 0)
            {
                SetStatus("");
            }
            else if (place != null)
            {
                SetStatus("Enter flies to " + place.Name);
            }
            else
            {
                SetStatus("No place called \"" + search.Text.Trim() + "\"");
            }
        }

        private void Search_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }
            e.SuppressKeyPress = true;
            // Places only: marker labels are ranks, and those are chosen from the list.
            if (Places.Find(search.Text) is Place place)
            {
                flightDestination = place;
                presets.SelectedIndex = -1;
                globe.FlyTo(place);
                SetStatus("Flying to " + place.Name);
                globe.Focus();
            }
        }

        private void Presets_SelectedChanged(object? sender, EventArgs e)
        {
            if (presets.SelectedItem is string name && Places.Find(name) is Place place)
            {
                flightDestination = place;
                globe.FlyTo(place);
                SetStatus("Flying to " + place.Name);
            }
        }

        private void Globe_MarkerClicked(object? sender, MarkerEventArgs e)
        {
            flightDestination = null;
            if (e.Marker.Tag is Finding finding)
            {
                findingsList.SelectFinding(finding);
                FindingClicked?.Invoke(this, Summarise(finding));
            }
        }

        /// <summary>The few fields of a finding that the chat carries with a question.</summary>
        public static FindingSummary Summarise(Finding finding) => new()
        {
            Id = finding.Id,
            Kind = finding.Kind,
            Rank = finding.Rank,
            Summary = finding.Summary,
            Significant = finding.Significant,
            CellId = finding.Cell.Id,
            LatMin = finding.Cell.LatMin,
            LatMax = finding.Cell.LatMax,
            LonMin = finding.Cell.LonMin,
            LonMax = finding.Cell.LonMax
        };

        /// <summary>
        ///  Marks the findings a Detective answer cites: the first is chosen and flown to, and
        ///  the cells of the others are outlined beside it. Ids not in the results are passed over.
        /// </summary>
        public void ShowCited(IReadOnlyList<string> findingIds)
        {
            var cited = findingIds.Select(id => results.Findings.FirstOrDefault(finding => finding.Id == id))
                .Where(finding => finding != null).Select(finding => finding!).ToList();
            if (cited.Count == 0 || !ShowFinding(cited[0].Id))
            {
                return;
            }
            foreach (var other in cited.Skip(1))
            {
                OutlineCell(other.Cell);
            }
            SetStatus(cited.Count == 1 ? "The Detective cited this finding" : "The Detective cited " + cited.Count.ToString(British) + " findings, outlined");
        }

        /// <summary>
        ///  Shows one of the Detective's lookups: the box a search named, drawn and flown to, or
        ///  the cells of the findings a lookup returned. These are what it read, not what its
        ///  answer rests on, and the status line says which.
        /// </summary>
        public void ShowStep(AskStep step)
        {
            flightDestination = null;
            presets.SelectedIndex = -1;
            if (activeZone != null)
            {
                ChooseZone(null, false);
            }
            globe.ClearRegions();
            globe.SelectMarker(null);

            var found = step.FindingIds.Select(id => results.Findings.FirstOrDefault(finding => finding.Id == id))
                .Where(finding => finding != null).Select(finding => finding!).ToList();
            foreach (var cell in found.Select(finding => finding.Cell).DistinctBy(cell => cell.Id))
            {
                globe.AddRegion(cell.LatMin, cell.LatMax, cell.LonMin, cell.LonMax, Theme.TextSecondary, "", 0f);
            }

            if (step.Box is AskBox box)
            {
                globe.AddRegion(box.LatMin, box.LatMax, box.LonMin, box.LonMax, Theme.Accent, "The Detective searched here", 0f);
                globe.FlyToRegion(box.LatMin, box.LatMax, box.LonMin, box.LonMax);
            }
            else if (found.Count > 0)
            {
                globe.FlyTo(found[0].Cell.CentreLatitude, found[0].Cell.CentreLongitude, Math.Max(globe.Zoom, 2.5));
            }
            SetStatus(found.Count == 0 ? "Where the Detective looked" : "The Detective read " + found.Count.ToString(British)
                + (found.Count == 1 ? " finding here" : " findings here"));
        }

        /// <summary>Switches the globe to one variable's layer. Returns false when the results have no layer for it.</summary>
        public bool ShowLayer(string variable)
        {
            int layer = results.Grids.FindIndex(grid => grid.Variable == variable);
            if (layer < 0)
            {
                return false;
            }
            layers.SelectedIndex = layer;
            overlayToggle.Checked = true;
            return true;
        }

        /// <summary>Selects a finding in the list and shows it on the globe. Returns false if it isn't in the results.</summary>
        public bool ShowFinding(string findingId)
        {
            var finding = results.Findings.FirstOrDefault(candidate => candidate.Id == findingId);
            if (finding == null)
            {
                return false;
            }

            // A finding asked for by name, such as one the Detective cited, may lie outside the chosen zone,
            // where the list does not hold it. Let go of the zone so that it can be shown.
            if (activeZone != null && !activeCells.Contains((finding.Cell.Row, finding.Cell.Column)))
            {
                ChooseZone(null, false);
            }
            sideTabs.SelectedIndex = 0;

            // Selecting shows it. If it is selected already the list has nothing to report, so show it here.
            if (findingsList.SelectedFinding?.Id == finding.Id)
            {
                ShowFinding(finding);
            }
            else
            {
                findingsList.SelectFinding(finding);
            }
            return true;
        }

        /// <summary>Opens the card about a clicked satellite: its picture, who runs it, what it carries and what VIGIL uses it for.</summary>
        private void Globe_SatelliteClicked(object? sender, SatelliteEventArgs e)
        {
            if (e.Satellite.Tag is not MissionInfo info)
            {
                return;
            }

            satelliteCard.SetContent(
                e.Satellite.Label,
                info.Standing,
                info.InScan ? Theme.Positive : Theme.TextSecondary,
                MissionSatellites.Picture(info),
                new[]
                {
                    ("For VIGIL", info.Role),
                    ("Run by", info.Operator),
                    ("Launched", info.Launched),
                    ("Carries", info.Instrument),
                    ("Orbit", MissionSatellites.OrbitText(e.Satellite))
                },
                MissionSatellites.Caveat + " Picture: NASA.");
            satelliteCard.Visible = true;
            satelliteCard.BringToFront();
            PositionOverlays();
        }

        // ----- Zones -----

        /// <summary>The scan grid, from the run record, or worked out from a layer when there is no run.</summary>
        private GridSettings ScanGrid()
        {
            if (results.Run?.Settings.Grid is GridSettings grid && grid.CellDegrees > 0 && grid.Rows > 0 && grid.Columns > 0)
            {
                return grid;
            }
            var layer = results.Grids.FirstOrDefault(candidate => candidate.Rows > 0 && candidate.Columns > 0);
            if (layer != null)
            {
                return new GridSettings
                {
                    CellDegrees = (layer.LatMax - layer.LatMin) / layer.Rows,
                    Rows = layer.Rows,
                    Columns = layer.Columns,
                    LatMin = layer.LatMin,
                    LonMin = layer.LonMin
                };
            }
            return new GridSettings { CellDegrees = 2.5, Rows = 72, Columns = 144, LatMin = -90, LonMin = -180 };
        }

        /// <summary>Whether a layer's cell is in the chosen zone. With no zone chosen, every cell is.</summary>
        private bool InActiveZone(GridLayer layer, int row, int column)
        {
            if (activeZone == null)
            {
                return true;
            }
            var grid = ScanGrid();
            int gridRow = (int)Math.Round((layer.LatMin - grid.LatMin) / grid.CellDegrees) + row;
            int gridColumn = (int)Math.Round((layer.LonMin - grid.LonMin) / grid.CellDegrees) + column;
            return activeCells.Contains((gridRow, gridColumn));
        }

        private List<(int Row, int Column)> CellsOf(Zone zone)
        {
            if (!cellsByZone.TryGetValue(zone.Id, out var cells))
            {
                cells = ZoneGeometry.Cells(zone.Outline(), ScanGrid());
                cellsByZone[zone.Id] = cells;
            }
            return cells;
        }

        private static string CellsText(int count) => count == 1 ? "1 grid cell" : count.ToString("N0", British) + " grid cells";

        /// <summary>The findings to list and mark: all of them, or those whose cell is in the chosen zone.</summary>
        private IReadOnlyList<Finding> VisibleFindings() => activeZone == null
            ? results.Findings
            : results.Findings.Where(finding => activeCells.Contains((finding.Cell.Row, finding.Cell.Column))).ToList();

        /// <summary>
        ///  Redraws everything that depends on the zones: their outlines on the globe, the list
        ///  of them, and the findings, markers and layer, which a chosen zone limits to its cells.
        /// </summary>
        private void ShowZones()
        {
            var grid = ScanGrid();
            activeCells = activeZone == null ? new HashSet<(int Row, int Column)>() : CellsOf(activeZone).ToHashSet();

            globe.ClearZones();
            var items = new List<ZoneListItem>();
            foreach (var zone in zones.Zones)
            {
                bool active = zone.Id == activeZone?.Id;
                // The chosen zone takes the accent, and shows the stepped edge of the cells it really covers.
                var shown = new GlobeZone(zone.Outline(), active ? Theme.Accent : Theme.TextSecondary)
                {
                    Label = zone.Name,
                    Emphasised = active,
                    Tag = zone
                };
                if (active)
                {
                    shown.CellEdges = ZoneGeometry.BoundaryEdges(activeCells, grid);
                }
                globe.AddZone(shown);
                items.Add(new ZoneListItem(zone, CellsText(CellsOf(zone).Count)));
            }
            zonePanel.SetZones(items, activeZone?.Id);

            var visible = VisibleFindings();
            findingsList.SetFindings(visible);
            findingsCard.Title = activeZone == null ? "Findings" : "Findings in " + activeZone.Name;
            scanText = ScanText(visible.Count);
            scanLine.Invalidate();
            PositionSyntheticBadge();

            globe.ClearRegions();
            globe.ClearMarkers();
            markersByFinding.Clear();
            AddFindingMarkers(visible);
            ShowSelectedLayer();
        }

        /// <summary>The line under the findings title: what the scan did, or how much of it the chosen zone holds.</summary>
        private string ScanText(int visible)
        {
            if (activeZone != null)
            {
                return string.Format(British, "{0:N0} of the {1:N0} findings listed are in this zone, which takes in {2}.",
                    visible, results.Findings.Count, CellsText(activeCells.Count));
            }
            if (results.Run is RunRecord run)
            {
                return string.Format(British, "{0:N0} of {1:N0} tests significant after correction, {2:N0} findings listed.",
                    run.Counts.TestsSignificant, run.Counts.TestsRun, results.Findings.Count);
            }
            return "";
        }

        /// <summary>Chooses a zone, or none. The findings, markers and layer follow, and the globe flies to it when asked.</summary>
        private void ChooseZone(Zone? zone, bool fly)
        {
            activeZone = zone;
            ShowZones();
            ShowingChanged?.Invoke(this, EventArgs.Empty);
            if (zone == null)
            {
                SetStatus("");
                return;
            }

            ShowZoneTrends(zone);
            if (fly && globe.Zones.FirstOrDefault(shown => ReferenceEquals(shown.Tag, zone)) is GlobeZone target)
            {
                flightDestination = null;
                presets.SelectedIndex = -1;
                globe.FlyTo(target);
            }
            SetStatus(zone.Name + ": " + CellsText(activeCells.Count) + ", " + findingsList.Findings.Count.ToString("N0", British)
                + (findingsList.Findings.Count == 1 ? " finding" : " findings"));
        }

        private void ZonePanel_DrawClicked(object? sender, EventArgs e)
        {
            if (globe.PenMode)
            {
                globe.ClearStroke();
                globe.PenMode = false;
                SetStatus("");
                return;
            }

            globe.ClearStroke();
            globe.PenMode = true;
            SetStatus("Drawing a zone");
            globe.Focus();
        }

        /// <summary>
        ///  The pen was lifted. An outline that can be a zone waits on the globe while it is
        ///  named. One that cannot is removed, with the reason, and the pen stays ready.
        /// </summary>
        private void Globe_ZoneDrawn(object? sender, ZoneDrawnEventArgs e)
        {
            if (ZoneGeometry.WrapsPole(e.Outline))
            {
                globe.ClearStroke();
                SetStatus("A zone cannot go all the way round a pole. Draw it again.");
                return;
            }

            int cells = ZoneGeometry.Cells(e.Outline, ScanGrid()).Count;
            if (cells == 0)
            {
                // The numbers for a zone come from whole grid cells, so an outline holding none has nothing to show.
                globe.ClearStroke();
                SetStatus("That outline holds no centre of a grid cell. Draw a larger area.");
                return;
            }

            pendingOutline = e.Outline;
            globe.PenMode = false;
            zoneNameBar.Begin(zones.NextName(), CellsText(cells));
            zoneNameBar.Visible = true;
            zoneNameBar.BringToFront();
            PositionOverlays();
            zoneNameBar.FocusName();
            SetStatus("");
        }

        private void SavePendingZone()
        {
            if (pendingOutline == null)
            {
                return;
            }

            var zone = new Zone
            {
                Name = zoneNameBar.ZoneName.Length > 0 ? zoneNameBar.ZoneName : zones.NextName(),
                Points = pendingOutline.Select(point => new ZonePoint { Latitude = point.Latitude, Longitude = point.Longitude }).ToList()
            };
            globe.ClearStroke();
            // Chosen before it is added, so the redraw that follows the add shows it in use.
            activeZone = zone;
            zones.Add(zone);
            ShowZoneTrends(zone);
            SetStatus(zone.Name + " saved: " + CellsText(activeCells.Count));
            globe.Focus();
        }

        private void Zones_ListChanged(object? sender, EventArgs e)
        {
            var saved = zones.Zones;
            if (activeZone != null && saved.All(zone => zone.Id != activeZone.Id))
            {
                activeZone = null;
                SetStatus("");
            }
            foreach (string id in cellsByZone.Keys.Where(id => saved.All(zone => zone.Id != id)).ToList())
            {
                cellsByZone.Remove(id);
                analysisByZone.Remove(id);
            }
            ShowZones();
        }

        /// <summary>
        ///  Asks the analysis service for the chosen zone's series and trends, and shows them
        ///  under the list of zones. The app works none of this out itself. When the service
        ///  cannot answer, or holds a different scan from the one on screen, it says so and
        ///  shows no figures.
        /// </summary>
        private async void ShowZoneTrends(Zone zone)
        {
            if (!analysisByZone.TryGetValue(zone.Id, out var analysis))
            {
                zonePanel.ShowZoneMessage(zone.Name, "Asking the analysis service for the trend over this zone…");
                try
                {
                    analysis = await service.ZoneAsync(zone.Points);
                }
                catch (Exception error)
                {
                    if (activeZone?.Id == zone.Id)
                    {
                        string reason = error is ServiceUnavailableException ? error.Message : "Something went wrong asking the service.";
                        zonePanel.ShowZoneMessage(zone.Name, "The trend over a zone comes from the analysis service at " + service.Address + ". "
                            + reason + " The findings and the layer are still limited to the zone.");
                    }
                    return;
                }
                analysisByZone[zone.Id] = analysis;
            }

            // Another zone may have been chosen, or this one deleted, while the answer was on its way.
            if (activeZone?.Id != zone.Id)
            {
                return;
            }
            if (analysis.RunId != results.Run?.Id)
            {
                zonePanel.ShowZoneMessage(zone.Name, "The analysis service holds a different scan from the one on screen, so its figures for "
                    + "this zone are not shown. Start the app again while the service is running to load the service's scan.");
                return;
            }
            // The outline on the globe and the figures must be about the same cells. The app and the service
            // each work the cells out by the same rule, and this is the check that they still agree.
            if (!activeCells.Select(cell => ZoneGeometry.CellId(cell.Row, cell.Column)).ToHashSet().SetEquals(analysis.Zone.Cells))
            {
                zonePanel.ShowZoneMessage(zone.Name, "The analysis service and the app do not agree on which grid cells this zone takes in, "
                    + "so its figures are not shown.");
                return;
            }
            zonePanel.ShowZoneAnalysis(zone.Name, analysis);
        }

        private void Globe_ZoneClicked(object? sender, ZoneEventArgs e)
        {
            if (e.Zone.Tag is Zone zone && zone.Id != activeZone?.Id)
            {
                ChooseZone(zone, false);
            }
        }

        private void Globe_RegionSelected(object? sender, RegionEventArgs e)
        {
            var r = e.Region;
            SetStatus(string.Format(British,
                "Selected {0:0.0}° to {1:0.0}° lat, {2:0.0}° to {3:0.0}° lon",
                r.LatitudeMin, r.LatitudeMax, r.LongitudeMin, r.LongitudeMax));
        }

        private void Globe_FlyToComplete(object? sender, FlightEventArgs e)
        {
            if (flightDestination is Place place)
            {
                SetStatus(place.Name + ": " + place.Description);
                flightDestination = null;
            }
        }
    }
}
