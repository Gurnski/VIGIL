using VIGIL.Controls;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Pages
{
    /// <summary>
    ///  The Overview page, where the app opens: what the scan covered and found in four
    ///  figures, each variable with its source and its results, the settings the scan ran with,
    ///  and the top of the ranking. Everything on it is read from the results document.
    /// </summary>
    // Built in code with no designer file, so Visual Studio opens it as code rather than trying to load a designer.
    [System.ComponentModel.DesignerCategory("Code")]
    public class OverviewPage : UserControl
    {
        private const int TopFindings = 8;

        private readonly ResultsDocument results;
        private readonly PageHeader header = new() { Title = "Overview", Dock = DockStyle.Top };
        private readonly Panel body = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
        private readonly TableLayoutPanel tiles = new() { ColumnCount = 4, RowCount = 1, BackColor = Theme.Background };
        private readonly Card summaryCard = new() { Title = "Variables" };
        private readonly StillPanel scroller = new() { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        private readonly ScanSummaryView summary = new();
        private readonly Card findingsCard = new() { Title = "Top of the ranking" };
        private readonly FindingsList list = new() { Dock = DockStyle.Fill };
        private readonly Panel actions = new() { Dock = DockStyle.Bottom, BackColor = Theme.Surface };
        private readonly VigilButton openMap = new() { Variant = ButtonVariant.Primary, IconGlyph = Icons.Globe, Text = "Open the map" };
        private readonly VigilButton openEvidence = new() { Variant = ButtonVariant.Secondary, IconGlyph = Icons.Chart, Text = "All the evidence" };
        private readonly Badge syntheticBadge = new() { Text = "Synthetic fixture", Tone = BadgeTone.Warning };

        /// <summary>Raised with a finding's id when one of the top findings is chosen.</summary>
        public event EventHandler<string>? FindingChosen;

        /// <summary>Raised with a variable's id when its row is clicked.</summary>
        public event EventHandler<string>? VariableChosen;

        public event EventHandler? MapRequested;
        public event EventHandler? EvidenceRequested;

        public OverviewPage(ResultsDocument results)
        {
            this.results = results;
            BackColor = Theme.Background;
            DoubleBuffered = true;

            header.Height = Px(72);
            header.Subtitle = results.Synthetic
                ? "These figures are a made-up fixture: start the analysis service before the app to see a real scan."
                : "What the scan covered, what it found, and how far each part has been checked.";

            BuildTiles();

            summary.SetResults(results);
            summary.VariableClicked += (s, variable) => VariableChosen?.Invoke(this, variable);
            scroller.Controls.Add(summary);
            scroller.ClientSizeChanged += (s, e) => summary.Width = scroller.ClientSize.Width;
            summaryCard.Controls.Add(scroller);

            list.SetFindings(results.Findings.Take(TopFindings).ToList());
            list.SelectedChanged += (s, e) =>
            {
                if (list.SelectedFinding is Finding finding)
                {
                    FindingChosen?.Invoke(this, finding.Id);
                }
            };
            actions.Height = Px(Theme.ControlHeight + Theme.Space3);
            openMap.SetBounds(0, Px(Theme.Space3), Px(150), Px(Theme.ControlHeight));
            openEvidence.SetBounds(openMap.Right + Px(Theme.Space2), Px(Theme.Space3), Px(168), Px(Theme.ControlHeight));
            openMap.Click += (s, e) => MapRequested?.Invoke(this, EventArgs.Empty);
            openEvidence.Click += (s, e) => EvidenceRequested?.Invoke(this, EventArgs.Empty);
            actions.Controls.Add(openMap);
            actions.Controls.Add(openEvidence);
            findingsCard.Controls.Add(list);
            findingsCard.Controls.Add(actions);

            // Made-up numbers are labelled wherever they are listed.
            syntheticBadge.Visible = results.Synthetic;
            findingsCard.Controls.Add(syntheticBadge);
            syntheticBadge.BringToFront();
            findingsCard.Resize += (s, e) =>
            {
                var titleLine = findingsCard.TitleBounds;
                syntheticBadge.Location = new Point(titleLine.Right - syntheticBadge.Width, titleLine.Y + (titleLine.Height - syntheticBadge.Height) / 2);
            };

            body.Controls.AddRange(new Control[] { tiles, summaryCard, findingsCard });
            body.Layout += (s, e) => LayoutBody();
            Controls.Add(body);
            Controls.Add(header);
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowChrome.UseDarkScrollbars(scroller);
            summary.Width = scroller.ClientSize.Width;
        }

        /// <summary>The four figures the demo opens with, each read from the run record.</summary>
        private void BuildTiles()
        {
            for (int i = 0; i < 4; i++)
            {
                tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            }
            tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var run = results.Run;
            var counts = run?.Counts ?? new RunCounts();
            string share = counts.TestsRun > 0 ? Numbers.Percent((double)counts.TestsSignificant / counts.TestsRun) + " of the tests run" : "";
            string kinds = string.Join(", ", counts.FindingsByKind.OrderByDescending(pair => pair.Value)
                .Select(pair => Numbers.Count(pair.Value) + " " + FindingsList.LabelText(pair.Key).ToLowerInvariant() + (pair.Value == 1 ? "" : "s")));

            var all = new List<StatTile>
            {
                new() { Caption = "Tests run", Value = Numbers.Count(counts.TestsRun), Arrow = TrendArrow.None, DeltaColour = Theme.TextSecondary,
                        Delta = "one variable in one cell, " + Numbers.Count(results.Variables.Count) + " variables" },
                new() { Caption = "Significant after correction", Value = Numbers.Count(counts.TestsSignificant), Arrow = TrendArrow.None,
                        DeltaColour = Theme.TextSecondary, Delta = share },
                new() { Caption = "Findings", Value = Numbers.Count(counts.FindingsTotal > 0 ? counts.FindingsTotal : results.Findings.Count),
                        Arrow = TrendArrow.None, DeltaColour = Theme.TextSecondary, Delta = kinds }
            };

            if (run?.Retest is RunRetest retest && retest.ByVariable.Count > 0)
            {
                int confirmed = retest.ByVariable.Sum(entry => entry.Confirmed);
                int tested = retest.ByVariable.Sum(entry => entry.Findings);
                int opposite = retest.ByVariable.Sum(entry => entry.Opposite);
                // The count re-tested is the headline. How many held means little pooled across variables,
                // whose second products differ in how readily they agree: each variable's row gives its own rate.
                all.Add(new StatTile
                {
                    Caption = "Trends tested again on a second product",
                    Value = Numbers.Count(tested),
                    Arrow = TrendArrow.None,
                    // Contradicted trends are what the re-test is for, so they take the colour.
                    DeltaColour = opposite > 0 ? Theme.Warning : Theme.TextSecondary,
                    Delta = Numbers.Count(confirmed) + " confirmed, " + Numbers.Count(opposite) + " contradicted, by variable below"
                });
            }
            else
            {
                all.Add(new StatTile
                {
                    Caption = "Standing",
                    Value = "Exploratory",
                    Arrow = TrendArrow.None,
                    DeltaColour = Theme.TextSecondary,
                    Delta = "nothing has been re-tested on a second product"
                });
            }

            for (int i = 0; i < all.Count; i++)
            {
                all[i].Dock = DockStyle.Fill;
                all[i].Margin = new Padding(0, 0, i < all.Count - 1 ? Px(Theme.Gutter) : 0, 0);
                tiles.Controls.Add(all[i]);
            }
        }

        /// <summary>The figures across the top; under them the variables on the left and the top findings on the right, stacked when narrow.</summary>
        private void LayoutBody()
        {
            int gap = Px(Theme.Gutter);
            int bottom = Px(Theme.Space4);
            var area = body.ClientRectangle;
            bool stacked = area.Width < Px(900);

            tiles.ColumnCount = 4;
            tiles.Bounds = new Rectangle(0, 0, area.Width, Px(132));
            int top = tiles.Bottom + gap;
            int height = Math.Max(0, area.Height - top - bottom);
            if (stacked)
            {
                summaryCard.Bounds = new Rectangle(0, top, area.Width, height * 3 / 5);
                findingsCard.Bounds = new Rectangle(0, summaryCard.Bottom + gap, area.Width, Math.Max(0, height - summaryCard.Height - gap));
                return;
            }
            int right = Math.Min(Px(440), area.Width * 2 / 5);
            summaryCard.Bounds = new Rectangle(0, top, area.Width - right - gap, height);
            findingsCard.Bounds = new Rectangle(summaryCard.Right + gap, top, right, height);
        }
    }
}
