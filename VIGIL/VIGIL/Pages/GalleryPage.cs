using VIGIL.Controls;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Pages
{
    /// <summary>
    ///  Reference sheet of every VIGIL control, filled with made-up demo data.
    ///  Built in code rather than the designer because it's a showcase, not a real screen.
    /// </summary>
    // Built in code with no designer file, so Visual Studio opens it as code rather than trying to load a designer.
    [System.ComponentModel.DesignerCategory("Code")]
    public class GalleryPage : UserControl
    {
        private readonly Random random = new(2026);
        private readonly TimeSeriesChart chart = new() { Dock = DockStyle.Fill, Units = "°C" };

        public GalleryPage()
        {
            BackColor = Theme.Background;
            AutoScroll = true;
            DoubleBuffered = true;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Theme.Background,
                Padding = new Padding(0, 0, Px(Theme.Space6), Px(Theme.Space6))
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            AddRow(layout, new PageHeader
            {
                Title = "Component gallery",
                Subtitle = "Every VIGIL control in one place. All numbers on this page are made up.",
                Height = Px(72)
            });
            AddRow(layout, BuildStatRow());
            AddRow(layout, BuildChartCard());
            AddRow(layout, BuildControlsCard());
            AddRow(layout, BuildFindingsCard());
            Controls.Add(layout);

            ShowDemoSeries();
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowChrome.UseDarkScrollbars(this);
        }

        private void AddRow(TableLayoutPanel layout, Control row)
        {
            row.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            row.Margin = new Padding(0, 0, 0, Px(Theme.Gutter));
            layout.Controls.Add(row);
        }

        private Control BuildStatRow()
        {
            var row = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 1,
                Height = Px(132),
                BackColor = Theme.Background
            };
            for (int i = 0; i < 3; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            }

            row.Controls.Add(new StatTile
            {
                Caption = "Land surface temperature",
                Value = "+0.21 °C",
                Delta = "per decade, p < 0.01",
                Arrow = TrendArrow.Up,
                DeltaColour = Theme.Warning,
                Sparkline = RandomWalk(40, 0.05)
            });
            row.Controls.Add(new StatTile
            {
                Caption = "Precipitation",
                Value = "−3.4 %",
                Delta = "per decade, p = 0.18",
                Arrow = TrendArrow.Down,
                DeltaColour = Theme.TextSecondary,
                Sparkline = RandomWalk(40, -0.01)
            });
            row.Controls.Add(new StatTile
            {
                Caption = "Vegetation (NDVI)",
                Value = "+0.012",
                Delta = "per decade, p < 0.05",
                Arrow = TrendArrow.Up,
                DeltaColour = Theme.Positive,
                Sparkline = RandomWalk(40, 0.03)
            });

            for (int i = 0; i < row.Controls.Count; i++)
            {
                var tile = row.Controls[i];
                tile.Dock = DockStyle.Fill;
                tile.Margin = new Padding(0, 0, i < 2 ? Px(Theme.Gutter) : 0, 0);
            }
            return row;
        }

        private Control BuildChartCard()
        {
            var card = new Card
            {
                Title = "Land surface temperature anomaly (demo data)",
                Size = new Size(Px(1000), Px(420))
            };
            card.Controls.Add(chart);

            var badge = new Badge { Text = "Significant trend, p < 0.01", Tone = BadgeTone.Positive };
            var titleLine = card.TitleBounds;
            badge.Location = new Point(titleLine.Right - badge.Width, titleLine.Y + (titleLine.Height - badge.Height) / 2);
            badge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            card.Controls.Add(badge);
            badge.BringToFront();
            return card;
        }

        private Control BuildControlsCard()
        {
            var card = new Card
            {
                Width = Px(1000),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            var sections = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Theme.Surface
            };
            sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            card.Controls.Add(sections);

            FlowLayoutPanel? lastFlow = null;
            void Section(string label, params Control[] controls)
            {
                sections.Controls.Add(new Divider
                {
                    Label = label,
                    Height = Px(16),
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Margin = new Padding(0, 0, 0, Px(Theme.Space3))
                });

                var flow = new FlowLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    BackColor = Theme.Surface,
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Margin = new Padding(0, 0, 0, Px(Theme.Space6))
                };
                foreach (var control in controls)
                {
                    control.Margin = new Padding(0, 0, Px(Theme.Space3), 0);
                    flow.Controls.Add(control);
                }
                sections.Controls.Add(flow);
                lastFlow = flow;
            }

            var analyse = new VigilButton { Text = "Analyse", IconGlyph = Icons.Play, Variant = ButtonVariant.Primary };
            analyse.Click += (s, e) => ShowDemoSeries();
            Section("Buttons",
                analyse,
                new VigilButton { Text = "Export", IconGlyph = Icons.Download, Variant = ButtonVariant.Secondary },
                new VigilButton { Text = "Reset", IconGlyph = Icons.Refresh, Variant = ButtonVariant.Ghost },
                new VigilButton { Text = "Disabled", Enabled = false });

            var trendToggle = new ToggleSwitch { Text = "Show trend line", Checked = true, Height = Px(32) };
            trendToggle.CheckedChanged += (s, e) => chart.ShowTrendLine = trendToggle.Checked;
            var cadence = new SegmentedControl();
            cadence.SetItems("Monthly", "Seasonal", "Annual");
            Section("Switches",
                trendToggle,
                new ToggleSwitch { Text = "Seasonal adjustment", Height = Px(32) },
                cadence);

            Section("Badges",
                new Badge { Text = "Not significant", Tone = BadgeTone.Neutral },
                new Badge { Text = "MODIS", Tone = BadgeTone.Accent },
                new Badge { Text = "Significant", Tone = BadgeTone.Positive },
                new Badge { Text = "Decreasing", Tone = BadgeTone.Negative },
                new Badge { Text = "Increasing", Tone = BadgeTone.Warning },
                new Badge { Text = "Live", Tone = BadgeTone.Accent, ShowDot = true });

            Section("Inputs",
                new SearchBox { Placeholder = "Search variables…", Width = Px(320) },
                new SpinnerControl { ForeColor = Theme.Accent, Size = new Size(Px(24), Px(24)), Margin = new Padding(0, Px(6), 0, 0) });

            if (lastFlow != null)
            {
                lastFlow.Margin = new Padding(0);
            }
            return card;
        }

        private Control BuildFindingsCard()
        {
            var card = new Card
            {
                Title = "Findings list (demo data)",
                Size = new Size(Px(1000), Px(320))
            };
            var list = new FindingsList { Dock = DockStyle.Fill };
            list.SetFindings(new[]
            {
                DemoFinding(1, FindingKind.Cascade, "Vegetation follows soil moisture by 2 months (r = +0.86). An association, not a cause.", true, 0.82, -8.75, -51.25, "GLDAS Noah", "Terra MODIS"),
                DemoFinding(2, FindingKind.Trend, "Air temperature at 2 m: warming at +0.72 K per decade", true, 0.71, 61.25, 101.25, "MERRA-2"),
                DemoFinding(3, FindingKind.ChangePoint, "Vegetation (NDVI): level shifts by +0.027 NDVI around 2010-06", true, 0.64, -26.25, 133.75, "Terra MODIS"),
                DemoFinding(4, FindingKind.Contradiction, "Vegetation (NDVI): greening here, browning in the next cell east", true, 0.58, 13.75, 3.75, "Terra MODIS"),
                DemoFinding(5, FindingKind.Trend, "Precipitation: no significant trend", false, 0.21, 51.25, 23.75, "GPM IMERG")
            });
            list.SelectedIndex = 1;
            card.Controls.Add(list);
            return card;
        }

        private static Finding DemoFinding(int rank, string kind, string summary, bool significant, double score,
            double latitude, double longitude, params string[] missions)
        {
            return new Finding
            {
                Id = "demo." + rank,
                Kind = kind,
                Rank = rank,
                Summary = summary,
                Significant = significant,
                Cell = new Cell { LatMin = latitude - 1.25, LatMax = latitude + 1.25, LonMin = longitude - 1.25, LonMax = longitude + 1.25 },
                Variables = missions.Select(mission => new Variable { Mission = mission }).ToList(),
                Score = new Score { Total = score }
            };
        }

        /// <summary>
        ///  Fake monthly temperature anomalies, 2000 to 2025: a warming trend, a seasonal cycle
        ///  and autocorrelated noise. Each call gives a slightly different series.
        /// </summary>
        private void ShowDemoSeries()
        {
            const int months = 26 * 12;
            double perYear = 0.015 + random.NextDouble() * 0.015;
            var dates = new DateTime[months];
            var values = new double[months];
            double noise = 0;

            for (int i = 0; i < months; i++)
            {
                noise = 0.6 * noise + (random.NextDouble() - 0.5) * 0.25;
                dates[i] = new DateTime(2000, 1, 15).AddMonths(i);
                values[i] = -0.2 + perYear * (i / 12.0) + 0.15 * Math.Sin(2 * Math.PI * i / 12) + noise;
            }
            chart.SetData(dates, values);
        }

        private double[] RandomWalk(int length, double drift)
        {
            var walk = new double[length];
            for (int i = 1; i < length; i++)
            {
                walk[i] = walk[i - 1] + drift + (random.NextDouble() - 0.5) * 0.2;
            }
            return walk;
        }
    }
}
