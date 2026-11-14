using VIGIL.Controls;
using VIGIL.Controls.Globe;
using VIGIL.Data;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Pages
{
    /// <summary>
    ///  The Map page: a 3D globe showing where data is and where it's changing, with a toolbar
    ///  to search for places, jump to presets and switch layers. Until real datasets are wired
    ///  in it carries demo overlays (markers, a region, a synthetic trend grid and a satellite
    ///  on its orbit), all labelled as such.
    /// </summary>
    // Built in code with no designer file, so Visual Studio opens it as code rather than trying to load a designer.
    [System.ComponentModel.DesignerCategory("Code")]
    public class MapPage : UserControl
    {
        private readonly GlobeControl globe = new() { Dock = DockStyle.Fill };
        private readonly SearchBox search = new() { Placeholder = "Fly to a place…" };
        private readonly SegmentedControl presets = new();
        private readonly ToggleSwitch graticuleToggle = new() { Text = "Grid lines" };
        private readonly ToggleSwitch rotateToggle = new() { Text = "Rotate", Checked = true };
        private readonly ToggleSwitch overlayToggle = new() { Text = "Overlay", Checked = true };
        private readonly Badge status = new() { Text = "", Tone = BadgeTone.Warning };
        private readonly ColourScaleLegend legend = new();

        private static readonly string[] PresetNames = { "Amazon", "Greenland", "Sahel", "Antarctica" };

        private Place? flightDestination;
        private readonly Dictionary<string, GlobeMarker> markersByCell = new();

        /// <summary>Raised when a finding's marker is clicked, so the Detective can be asked about it.</summary>
        public event EventHandler<FindingSummary>? FindingClicked;

        public MapPage()
        {
            BackColor = Theme.Background;
            DoubleBuffered = true;

            // The fill control goes in first so the bars, docked after it, take the top.
            Controls.Add(globe);
            Controls.Add(BuildToolbar());
            Controls.Add(new PageHeader
            {
                Title = "Map",
                Subtitle = "Drag to rotate, scroll to zoom, right-drag to tilt. Click a finding to ask the Detective about it.",
                Dock = DockStyle.Top,
                Height = Px(72)
            });

            legend.Size = new Size(Px(240), Px(68));
            legend.Title = "Demo trend overlay";
            legend.Units = "°C/decade";
            legend.Follow(globe);
            globe.Controls.Add(legend);
            globe.Controls.Add(status);

            globe.MarkerClicked += Globe_MarkerClicked;
            globe.RegionSelected += Globe_RegionSelected;
            globe.FlyToComplete += Globe_FlyToComplete;
            // Any hand on the globe cancels a flight without FlyToComplete, so forget where it was going.
            globe.MouseDown += (s, e) => flightDestination = null;
            globe.MouseWheel += (s, e) => flightDestination = null;
            globe.KeyDown += (s, e) => flightDestination = null;
            globe.Resize += (s, e) => PositionOverlays();
            PositionOverlays();

            AddDemoOverlays();
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

            presets.Font = Theme.Caption;
            presets.SetItems(PresetNames);
            presets.SelectedIndex = -1;
            presets.Height = Px(Theme.ControlHeight);
            presets.Margin = new Padding(0, 0, Px(Theme.Space3), Px(Theme.Space2));
            presets.SelectedChanged += Presets_SelectedChanged;
            bar.Controls.Add(presets);

            // The three switches sit in a group of their own, so they wrap to the next row together
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
            foreach (var toggle in new[] { graticuleToggle, rotateToggle, overlayToggle })
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
            overlayToggle.CheckedChanged += (s, e) => globe.GridOverlayOpacity = overlayToggle.Checked ? 0.65f : 0f;

            return bar;
        }

        /// <summary>Legend top-left and the status badge top-right; the globe's own HUD has the bottom corners.</summary>
        private void PositionOverlays()
        {
            int edge = Px(Theme.Space4);
            legend.Location = new Point(edge, edge);
            // On a small globe the legend would sit on the planet, and the status badge would sit on the legend.
            legend.Visible = globe.Width >= Px(620) && globe.Height >= Px(420);
            int statusLeft = globe.Width - edge - status.Width;
            bool clear = !legend.Visible || statusLeft >= legend.Right + edge;
            status.Location = clear ? new Point(Math.Max(edge, statusLeft), edge) : new Point(edge, legend.Bottom + Px(Theme.Space2));
        }

        private void SetStatus(string text)
        {
            status.Text = text;
            PositionOverlays();
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
            if (globe.FlyTo(search.Text))
            {
                flightDestination = Places.Find(search.Text);
                presets.SelectedIndex = -1;
                SetStatus("Flying to " + (flightDestination?.Name ?? search.Text.Trim()));
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
            if (e.Marker.Tag is FindingSummary finding)
            {
                SetStatus(finding.KindLabel + " in cell " + finding.CellId);
                FindingClicked?.Invoke(this, finding);
            }
        }

        /// <summary>Flies to a finding and selects its marker. Returns false if it isn't on the map.</summary>
        public bool ShowFinding(string findingId)
        {
            var finding = Fixture.FindFinding(findingId);
            if (finding == null || !markersByCell.TryGetValue(finding.CellId, out var marker))
            {
                return false;
            }

            flightDestination = null;
            presets.SelectedIndex = -1;
            globe.SelectMarker(marker, fly: true);
            SetStatus(finding.KindLabel + " in cell " + finding.CellId);
            return true;
        }

        /// <summary>
        ///  One marker per grid cell that has a finding, carrying the cell's top-ranked finding.
        ///  They come from the contract's fixture, so they are made up and labelled as samples.
        /// </summary>
        private void AddFindingMarkers()
        {
            foreach (var cell in Fixture.Findings.GroupBy(f => f.CellId))
            {
                var top = cell.OrderBy(f => f.Rank).First();
                int others = cell.Count() - 1;
                string label = top.KindLabel + (others > 0 ? " and " + others + " more" : "");
                var marker = globe.AddMarker(top.Latitude, top.Longitude, label,
                    top.Significant ? Theme.Series : Theme.TextSecondary, "Sample result. " + top.Summary);
                marker.Tag = top;
                markersByCell[top.CellId] = marker;
            }
        }

        private void Globe_RegionSelected(object? sender, RegionEventArgs e)
        {
            var r = e.Region;
            SetStatus(string.Format(System.Globalization.CultureInfo.GetCultureInfo("en-GB"),
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

        /// <summary>
        ///  Made-up content standing in for real datasets: example sites, a study region, a
        ///  synthetic warming trend per 2° cell and a satellite on a sun-synchronous orbit.
        /// </summary>
        private void AddDemoOverlays()
        {
            AddFindingMarkers();

            globe.AddRegion(10, 20, -18, 40, Theme.Warning, "Sahel study region (demo)", 0.16f);

            globe.SetGridOverlay(DemoTrendGrid(), -90, 90, -180, 180, ColourScale.BlueWhiteRed, -1, 1, 0.65f, smooth: true);

            // AppEEARS orbit: the plane is where a 10:30 sun-synchronous satellite's really is
            globe.AddSatellite(new GlobeSatellite("AppEEARS, NASA", 98.2, 705, Theme.Positive)
            {
                AscendingNodeLocalTime = 10.5,
                TimeScale = 1
            });
        }

        /// <summary>
        ///  A plausible-looking warming pattern on a 2° grid: strongest in the Arctic, a cool
        ///  patch in the Southern Ocean, some regional texture and noise. Antarctica's interior
        ///  is left as NaN to show how missing data appears.
        /// </summary>
        private static double[,] DemoTrendGrid()
        {
            var random = new Random(42);
            const int rows = 90, columns = 180;
            var grid = new double[rows, columns];
            for (int r = 0; r < rows; r++)
            {
                double lat = -90 + (r + 0.5) * 180.0 / rows;
                double latRad = lat * Math.PI / 180;
                for (int c = 0; c < columns; c++)
                {
                    double lon = -180 + (c + 0.5) * 360.0 / columns;
                    double lonRad = lon * Math.PI / 180;
                    if (lat < -80)
                    {
                        grid[r, c] = double.NaN;
                        continue;
                    }

                    double arctic = 0.75 * Math.Pow((lat + 90) / 180, 3.5) - 0.04;
                    double southernOcean = -0.55 * Math.Exp(-Math.Pow((lat + 58) / 9, 2)) * (0.6 + 0.4 * Math.Cos(lonRad - 1));
                    double mediterranean = 0.45 * Math.Exp(-Math.Pow((lat - 36) / 11, 2) - Math.Pow((lon - 28) / 30, 2));
                    double westernAmerica = 0.4 * Math.Exp(-Math.Pow((lat - 42) / 10, 2) - Math.Pow((lon + 112) / 16, 2));
                    double tropicalPacific = -0.3 * Math.Exp(-Math.Pow(lat / 8, 2) - Math.Pow((lon + 130) / 30, 2));
                    double texture = 0.12 * Math.Sin(3 * lonRad + 1) * Math.Cos(2 * latRad);
                    double noise = (random.NextDouble() - 0.5) * 0.08;
                    grid[r, c] = Math.Clamp(arctic + southernOcean + mediterranean + westernAmerica + tropicalPacific + texture + noise, -1, 1);
                }
            }
            return grid;
        }
    }
}
