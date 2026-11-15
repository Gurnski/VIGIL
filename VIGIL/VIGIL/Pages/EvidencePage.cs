using VIGIL.Controls;
using VIGIL.Controls.Evidence;
using VIGIL.Data;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Pages
{
    /// <summary>
    ///  The Evidence page: the ranked findings on the left, and on the right everything the
    ///  results say about the chosen one. A finding whose series is not in the results document
    ///  is fetched from the analysis service, and "Recompute" asks the service to work its cell
    ///  out again from the gridded data, so a stored figure can be set beside a fresh one.
    /// </summary>
    // Built in code with no designer file, so Visual Studio opens it as code rather than trying to load a designer.
    [System.ComponentModel.DesignerCategory("Code")]
    public class EvidencePage : UserControl
    {
        private static readonly string[] KindNames = { "All", "Trends", "Change points", "Cascades", "Contradictions" };
        private static readonly string?[] Kinds = { null, FindingKind.Trend, FindingKind.ChangePoint, FindingKind.Cascade, FindingKind.Contradiction };

        private readonly ResultsDocument results;
        private readonly ResultsClient service;
        private readonly Dictionary<string, Finding> byId;
        // The series fetched from the service this session, beside those the document came with.
        private readonly List<Series> fetched = new();
        private readonly HashSet<string> asked = new();

        private readonly PageHeader header = new()
        {
            Title = "Evidence",
            Subtitle = "Every finding with its series, its uncertainty, its method, its sources and what it cannot say.",
            Dock = DockStyle.Top
        };
        private readonly Panel body = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
        private readonly SegmentedControl kinds = new();
        private readonly Card listCard = new() { Title = "Findings" };
        private readonly FindingsList list = new() { Dock = DockStyle.Fill };
        private readonly Card viewCard = new();
        private readonly StillPanel scroller = new() { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        private readonly EvidenceView view = new();

        /// <summary>Raised with a finding's id by "Show on the map".</summary>
        public event EventHandler<string>? ShowOnMapRequested;

        /// <summary>Raised by "Ask the Detective", with the finding to attach to the question.</summary>
        public event EventHandler<Finding>? AskRequested;

        /// <summary>Raised with the finding whenever one is put on the page, for a presenter's audience to follow.</summary>
        public event EventHandler<Finding>? FindingShown;

        // The findings as a file for other tools. Every finding goes in, whatever the filter shows.
        private readonly VigilButton exportCsv = new() { Variant = ButtonVariant.Ghost, IconGlyph = Icons.Download, Text = "Export CSV" };
        private readonly VigilButton exportGeoJson = new() { Variant = ButtonVariant.Ghost, IconGlyph = Icons.Download, Text = "Export GeoJSON" };
        private readonly Label exportNote = new() { AutoSize = false, BackColor = Theme.Background, ForeColor = Theme.TextSecondary, Font = Theme.Caption, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolTip exportTip = new();

        public EvidencePage(ResultsDocument results, ResultsClient service)
        {
            this.results = results;
            this.service = service;
            byId = results.Findings.ToDictionary(finding => finding.Id);
            BackColor = Theme.Background;
            DoubleBuffered = true;

            header.Height = Px(72);

            kinds.Font = Theme.Caption;
            kinds.SetItems(KindNames);
            kinds.Height = Px(32);
            kinds.SelectedChanged += (s, e) => ShowList();

            listCard.Controls.Add(list);
            list.SelectedChanged += (s, e) =>
            {
                if (list.SelectedFinding is Finding finding)
                {
                    Show(finding);
                }
            };

            scroller.Controls.Add(view);
            scroller.ClientSizeChanged += (s, e) => view.Width = scroller.ClientSize.Width;
            viewCard.Controls.Add(scroller);
            view.ShowOnMapClicked += (s, finding) => ShowOnMapRequested?.Invoke(this, finding.Id);
            view.AskClicked += (s, finding) => AskRequested?.Invoke(this, finding);
            view.FindingClicked += (s, id) => ShowFinding(id);
            view.RecomputeClicked += (s, finding) => Recompute(finding);
            view.CopyClicked += (s, finding) =>
            {
                try
                {
                    Clipboard.SetText(EvidenceReport.Write(finding, results.Run, results.Synthetic));
                    view.ShowNote(finding, "Copied this finding as plain text, with every figure as the service stored it.");
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    view.ShowNote(finding, "The clipboard is in use by another program, so nothing was copied. Try again.");
                }
            };

            exportCsv.Click += (s, e) => Export("CSV", "csv", FindingsExport.ToCsv);
            exportGeoJson.Click += (s, e) => Export("GeoJSON", "geojson", FindingsExport.ToGeoJson);
            exportTip.SetToolTip(exportCsv, "Every finding as a table, for a spreadsheet or a notebook.");
            exportTip.SetToolTip(exportGeoJson, "Every finding with its grid cell as a shape, for QGIS and other mapping tools.");

            body.Controls.AddRange(new Control[] { kinds, exportCsv, exportGeoJson, exportNote, listCard, viewCard });
            body.Layout += (s, e) => LayoutBody();
            Controls.Add(body);
            Controls.Add(header);

            ShowList();
            // Opens on the top-ranked finding: whatever the ranking put first, not a case picked in advance.
            if (results.Findings.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        }

        private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        /// <summary>
        ///  Saves every finding as a file for other tools, in the contract's columns. Made-up
        ///  findings carry "synthetic-fixture" in the file's name and a column that says so.
        /// </summary>
        private void Export(string kind, string ending, Func<ResultsDocument, string> write)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Export the findings as " + kind,
                FileName = FindingsExport.FileName(results) + "." + ending,
                Filter = kind + " file (*." + ending + ")|*." + ending,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            {
                return;
            }
            try
            {
                // UTF-8 with no byte order mark: QGIS and pandas both read it as it is.
                File.WriteAllText(dialog.FileName, write(results), new System.Text.UTF8Encoding(false));
                exportNote.ForeColor = Theme.TextSecondary;
                exportNote.Text = "Saved " + Numbers.Count(results.Findings.Count) + " findings to " + Path.GetFileName(dialog.FileName);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                exportNote.ForeColor = Theme.Warning;
                exportNote.Text = "Could not save there: " + error.Message;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WindowChrome.UseDarkScrollbars(scroller);
            view.Width = scroller.ClientSize.Width;
        }

        /// <summary>The list on the left at a fixed width, the evidence in the room that is left; stacked when the page is narrow.</summary>
        private void LayoutBody()
        {
            int gap = Px(Theme.Gutter);
            int bottom = Px(Theme.Space4);
            var area = body.ClientRectangle;
            bool stacked = area.Width < Px(820);
            int listWidth = stacked ? area.Width : Px(380);
            int listHeight = stacked ? Math.Min(Px(260), area.Height / 3) : area.Height - bottom;

            // The filter runs across the top at its own width: it is wider than the list under it.
            kinds.Location = new Point(0, 0);
            // The exports sit beside the filter while there is room, and are left out on a page too narrow for them.
            int exportLeft = kinds.Right + Px(Theme.Space4);
            exportCsv.Bounds = new Rectangle(exportLeft, 0, Px(128), kinds.Height);
            exportGeoJson.Bounds = new Rectangle(exportCsv.Right + Px(Theme.Space1), 0, Px(156), kinds.Height);
            exportNote.Bounds = new Rectangle(exportGeoJson.Right + Px(Theme.Space3), 0, Math.Max(0, area.Width - exportGeoJson.Right - Px(Theme.Space3)), kinds.Height);
            exportCsv.Visible = exportGeoJson.Visible = exportGeoJson.Right <= area.Width;
            exportNote.Visible = exportCsv.Visible && exportNote.Width > Px(120);
            int top = kinds.Bottom + Px(Theme.Space3);
            listCard.Bounds = new Rectangle(0, top, listWidth, Math.Max(0, listHeight - top));
            viewCard.Bounds = stacked
                ? new Rectangle(0, listCard.Bottom + gap, area.Width, Math.Max(0, area.Height - listCard.Bottom - gap - bottom))
                : new Rectangle(listWidth + gap, top, Math.Max(0, area.Width - listWidth - gap), Math.Max(0, area.Height - bottom - top));
        }

        private void ShowList()
        {
            string? kind = kinds.SelectedIndex >= 0 && kinds.SelectedIndex < Kinds.Length ? Kinds[kinds.SelectedIndex] : null;
            var shown = kind == null ? results.Findings : results.Findings.Where(finding => finding.Kind == kind).ToList();
            var current = view.Finding;
            list.SetFindings(shown);
            listCard.Title = kind == null
                ? "Findings, " + Numbers.Count(shown.Count)
                : KindNames[kinds.SelectedIndex] + ", " + Numbers.Count(shown.Count) + " of " + Numbers.Count(results.Findings.Count);
            if (current != null && shown.Any(finding => finding.Id == current.Id))
            {
                list.SelectFinding(current);
            }
            else if (shown.Count == 0)
            {
                view.ShowMessage("The results hold no findings of this kind.");
            }
        }

        /// <summary>Opens a finding by its id. Returns false when it is not in the results.</summary>
        public bool ShowFinding(string findingId)
        {
            if (!byId.TryGetValue(findingId, out var finding))
            {
                return false;
            }
            if (!list.Findings.Any(listed => listed.Id == findingId))
            {
                kinds.SelectedIndex = 0;
            }
            if (list.SelectedFinding?.Id == findingId)
            {
                Show(finding);
            }
            else
            {
                list.SelectFinding(finding);
            }
            return true;
        }

        /// <summary>The cells and variables a finding's charts need, as the contract lists them.</summary>
        private static IEnumerable<(string CellId, string Variable)> Needed(Finding finding)
        {
            if (finding.Kind == FindingKind.Contradiction && finding.Contradiction is Contradiction pair)
            {
                yield return (finding.Cell.Id, pair.Variable);
                yield return (pair.PairedCell.Id, pair.Variable);
                yield break;
            }
            foreach (var variable in finding.Variables)
            {
                yield return (finding.Cell.Id, variable.Id);
            }
        }

        private IReadOnlyList<Series> SeriesHeld() => results.Series.Concat(fetched).ToList();

        private void Show(Finding finding, string? missing = null)
        {
            var held = SeriesHeld();
            bool complete = Needed(finding).All(need => held.Any(series => series.CellId == need.CellId && series.Variable == need.Variable));
            view.ShowFinding(finding, held, results.Run, results.Synthetic,
                missing ?? "Fetching this series from the analysis service…", id => byId.GetValueOrDefault(id));
            scroller.AutoScrollPosition = Point.Empty;
            FindingShown?.Invoke(this, finding);
            if (!complete && asked.Add(finding.Id))
            {
                FetchSeries(finding);
            }
            else if (!complete && missing == null)
            {
                Show(finding, MissingText("The service could not supply it."));
            }
        }

        private string MissingText(string reason) =>
            "This series is not in the results the app was started with, which carry one for the top of the ranking only. "
            + "The rest come from the analysis service at " + service.Address + ". " + reason;

        /// <summary>
        ///  Fetches the series a finding needs from the service, then shows the finding again
        ///  with them. When the service cannot be reached, the view says so where the chart would be.
        /// </summary>
        private async void FetchSeries(Finding finding)
        {
            string? reason = null;
            try
            {
                var document = await service.FindingAsync(finding.Id);
                // A series is only as good as the scan it came from: one from another run must not sit under this finding.
                if (results.Run != null && document.Findings.FirstOrDefault()?.Id != finding.Id)
                {
                    reason = "The service does not hold this finding, so it is running a different scan from the one on screen.";
                }
                else
                {
                    fetched.AddRange(document.Series.Where(series => !fetched.Any(have => have.CellId == series.CellId && have.Variable == series.Variable)));
                    if (document.Series.Count == 0)
                    {
                        reason = "The service holds the finding but not the gridded data its series is built from.";
                    }
                }
            }
            catch (Exception error)
            {
                reason = error is ServiceUnavailableException ? error.Message : "Something went wrong asking the service.";
            }

            if (view.Finding?.Id == finding.Id)
            {
                Show(finding, MissingText(reason ?? "The service could not supply it."));
            }
        }

        /// <summary>
        ///  Asks the service to run the analysis again on the finding's own cell, from the
        ///  gridded data, and sets its slope, interval and p-value beside the stored ones. The
        ///  app compares the two sets of figures and works out neither.
        /// </summary>
        private async void Recompute(Finding finding)
        {
            if (finding.Result is not TrendResult stored)
            {
                return;
            }
            view.ShowRecomputed(finding, "Asking the analysis service to work this cell out again from the gridded data…");
            string text;
            try
            {
                var cell = finding.Cell;
                var fresh = await service.AnalyseAsync(cell.LatMin, cell.LatMax, cell.LonMin, cell.LonMax);
                var again = fresh.Findings.FirstOrDefault(candidate => candidate.Id == finding.Id)?.Result;
                if (again == null)
                {
                    text = "The service ran the cell again and did not return this finding, so there is nothing to compare.";
                }
                else
                {
                    bool same = Same(again.SlopePerDecade, stored.SlopePerDecade) && Same(again.CiLow, stored.CiLow)
                        && Same(again.CiHigh, stored.CiHigh) && Same(again.PValue, stored.PValue);
                    text = "Recomputed from the gridded data just now (run " + (fresh.Run?.Id ?? "unnamed") + "): "
                        + Numbers.Signed(again.SlopePerDecade) + " " + again.Units + ", interval " + Numbers.Signed(again.CiLow) + " to "
                        + Numbers.Signed(again.CiHigh) + ", p before correction " + Numbers.P(again.PValue) + ". "
                        + (same ? "These are the stored figures, to the last digit the app holds." : "These differ from the stored figures above, so the service's data is not the data the stored scan was made from.")
                        + " The p-value after correction is not compared: a run on one cell has no other tests to correct for.";
                }
            }
            catch (Exception error)
            {
                text = "Recomputing needs the analysis service at " + service.Address + " with its gridded data. "
                    + (error is ServiceUnavailableException ? error.Message : "Something went wrong asking the service.");
            }
            view.ShowRecomputed(finding, text);
        }

        /// <summary>Whether two figures agree to within the last digits a round trip through JSON can change.</summary>
        private static bool Same(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    }
}
