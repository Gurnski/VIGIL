using System.Globalization;
using VIGIL.Controls;
using VIGIL.Controls.Globe;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Pages
{
    /// <summary>
    ///  The Map page: a 3D globe carrying one variable's trend per cell, beside the ranked list
    ///  of findings. Choosing a finding, in the list or by its marker, switches to its variable,
    ///  outlines its cell and flies there. A toolbar searches for places, jumps to presets and
    ///  switches layers. The results come from the document handed in at startup.
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

        private readonly ResultsDocument results;
        private readonly Dictionary<string, GlobeMarker> markersByFinding = new();

        private readonly GlobeControl globe = new() { Dock = DockStyle.Fill };
        private readonly SearchBox search = new() { Placeholder = "Fly to a place…" };
        private readonly SegmentedControl layers = new();
        private readonly SegmentedControl presets = new();
        private readonly ToggleSwitch graticuleToggle = new() { Text = "Grid lines" };
        private readonly ToggleSwitch rotateToggle = new() { Text = "Rotate", Checked = true };
        private readonly ToggleSwitch overlayToggle = new() { Text = "Layer", Checked = true };
        private readonly ToggleSwitch significantToggle = new() { Text = "Significant only" };
        private readonly Badge status = new() { Text = "", Tone = BadgeTone.Warning };
        private readonly ColourScaleLegend legend = new();
        private readonly Panel findingsHost = new();
        private readonly Card findingsCard = new() { Title = "Findings", Dock = DockStyle.Fill };
        private readonly FindingsList findingsList = new() { Dock = DockStyle.Fill };
        private readonly Badge syntheticBadge = new() { Text = "Synthetic fixture", Tone = BadgeTone.Warning };
        private readonly ToggleSwitch satellitesToggle = new() { Text = "Satellites", Checked = true };
        private readonly ToolTip satellitesTip = new() { InitialDelay = 400 };
        private readonly SatelliteCard satelliteCard = new() { Visible = false };

        private Place? flightDestination;
        private bool? findingsBelow;

        /// <summary>Raised when a finding's marker is clicked, so the Detective can be asked about it.</summary>
        public event EventHandler<FindingSummary>? FindingClicked;

        public MapPage(ResultsDocument results)
        {
            this.results = results;
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

            AddFindingMarkers();
            ShowSatellites(true);
            ShowSelectedLayer();
            ArrangeFindings();
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

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
            layers.SetItems(results.Grids.Select(layer => FindingsList.LabelText(layer.Variable)).ToArray());
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
            foreach (var toggle in new[] { overlayToggle, significantToggle, satellitesToggle, graticuleToggle, rotateToggle })
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
            significantToggle.CheckedChanged += (s, e) => ShowSelectedLayer();
            satellitesToggle.CheckedChanged += (s, e) => ShowSatellites(satellitesToggle.Checked);
            satellitesTip.SetToolTip(satellitesToggle, MissionSatellites.Caveat);

            return bar;
        }

        /// <summary>The findings card: what the scan did in a line, then the ranked list.</summary>
        private Control BuildFindings()
        {
            findingsHost.BackColor = Theme.Background;
            findingsHost.Controls.Add(findingsCard);

            findingsList.SetFindings(results.Findings);
            findingsList.SelectedChanged += FindingsList_SelectedChanged;
            findingsCard.Controls.Add(findingsList);

            if (results.Run is RunRecord run)
            {
                var counts = run.Counts;
                string scan = string.Format(British, "{0:N0} of {1:N0} tests significant after correction, {2:N0} findings listed.",
                    counts.TestsSignificant, counts.TestsRun, results.Findings.Count);
                // Drawn by hand: a Label insets its text by a few pixels, which would put it out of line with the card title.
                var scanLine = new Panel { Dock = DockStyle.Top, Height = Px(40), BackColor = Theme.Surface };
                scanLine.Paint += (s, e) => Draw.Text(e.Graphics, scan, Theme.Caption, scanLine.ClientRectangle, Theme.TextSecondary,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                scanLine.Resize += (s, e) => scanLine.Invalidate();
                findingsCard.Controls.Add(scanLine);
            }

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
            status.Location = clear ? new Point(Math.Max(edge, statusLeft), edge) : new Point(edge, legend.Bottom + Px(Theme.Space2));
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
                    bool shown = slope != null && (!significantOnly || adjusted < threshold);
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
            double unit = Math.Pow(10, Math.Floor(Math.Log10(limit)) - 1);
            return Math.Ceiling(limit / unit - 1e-9) * unit;
        }

        /// <summary>
        ///  One marker per cell that has a finding, labelled with the ranks found there and
        ///  carrying the top-ranked one. Several findings often share a cell.
        /// </summary>
        private void AddFindingMarkers()
        {
            foreach (var cell in results.Findings.Take(MaxMarkedFindings).GroupBy(finding => finding.Cell.Id))
            {
                var here = cell.ToList();
                var top = here[0];
                string label = "#" + top.Rank.ToString(British) + (here.Count > 1 ? " and " + (here.Count - 1).ToString(British) + " more" : "");
                // Accent because a marker can be acted on; the quieter colour where nothing in the cell is significant.
                var colour = here.Any(finding => finding.Significant) ? Theme.Accent : Theme.TextSecondary;
                var marker = globe.AddMarker(top.Cell.CentreLatitude, top.Cell.CentreLongitude, label, colour, top.Summary);
                marker.Tag = top;
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
                foreach (var satellite in MissionSatellites.Create())
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
            if (findingsList.SelectedFinding is Finding finding)
            {
                ShowFinding(finding);
            }
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
        private static FindingSummary Summarise(Finding finding) => new()
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

        /// <summary>Selects a finding in the list and shows it on the globe. Returns false if it isn't in the results.</summary>
        public bool ShowFinding(string findingId)
        {
            var finding = results.Findings.FirstOrDefault(candidate => candidate.Id == findingId);
            if (finding == null)
            {
                return false;
            }

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
                info.InScan ? "Its data is in the first scan" : "Not in the scan: behind a source kept for cross-checks",
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
