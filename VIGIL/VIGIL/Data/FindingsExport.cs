using System.Globalization;
using System.Text;
using System.Text.Json;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>
    ///  Writes the findings of a results document as a file for other tools: CSV for a
    ///  spreadsheet or a notebook, GeoJSON for QGIS. The columns and their order are the
    ///  contract's ("Findings as a file for other tools"), and the web viewer writes the same.
    ///  Nothing is worked out here: every value is copied from the document.
    /// </summary>
    public static class FindingsExport
    {
        /// <summary>The columns, in the contract's order.</summary>
        public static readonly IReadOnlyList<string> Columns = new[]
        {
            "rank", "id", "kind", "significant", "summary", "variables",
            "cell_id", "lat_centre", "lon_centre", "lat_min", "lat_max", "lon_min", "lon_max",
            "period_start", "period_end",
            "slope_per_decade", "units", "ci_low", "ci_high", "ci_level", "p_value", "p_adjusted",
            "change_date", "shift", "lag_months", "correlation", "paired_cell_id",
            "standing", "retest_outcome", "retest_product", "score", "run_id", "synthetic"
        };

        /// <summary>The file's name without its ending. Made-up findings say so in the name.</summary>
        public static string FileName(ResultsDocument results) =>
            "vigil-findings-" + (results.Synthetic || results.Run == null ? "synthetic-fixture" : results.Run.Id);

        /// <summary>One finding as the contract's columns. A value is a string, a number, a boolean or null.</summary>
        public static IReadOnlyList<object?> Row(Finding finding, ResultsDocument results)
        {
            var cell = finding.Cell;
            var trend = finding.Result;
            var change = finding.ChangePoint;
            var cascade = finding.Cascade;
            var contradiction = finding.Contradiction;
            var retest = finding.Standing.Retest;
            return new object?[]
            {
                finding.Rank, finding.Id, finding.Kind, finding.Significant, finding.Summary,
                string.Join("+", finding.Variables.Select(variable => variable.Id)),
                cell.Id, (cell.LatMin + cell.LatMax) / 2, (cell.LonMin + cell.LonMax) / 2, cell.LatMin, cell.LatMax, cell.LonMin, cell.LonMax,
                Month(finding.Period.Start), Month(finding.Period.End),
                trend?.SlopePerDecade ?? contradiction?.SlopePerDecade,
                trend?.Units ?? contradiction?.Units ?? change?.Units,
                trend?.CiLow, trend?.CiHigh, trend?.CiLevel,
                trend?.PValue ?? cascade?.PValue, trend?.PAdjusted ?? cascade?.PAdjusted,
                change == null ? null : Month(change.Date), change?.Shift,
                cascade?.LagMonths, cascade?.Correlation,
                contradiction?.PairedCell.Id,
                finding.Standing.Status, retest?.Outcome,
                retest == null ? null : (retest.Product.Mission + " " + retest.Product.Product).Trim(),
                finding.Score.Total, results.Run?.Id, results.Synthetic
            };
        }

        /// <summary>The findings as CSV: UTF-8, a header row, then one row per finding in rank order.</summary>
        public static string ToCsv(ResultsDocument results)
        {
            var text = new StringBuilder();
            text.Append(string.Join(",", Columns)).Append("\r\n");
            foreach (var finding in results.Findings.OrderBy(finding => finding.Rank))
            {
                text.Append(string.Join(",", Row(finding, results).Select(Field))).Append("\r\n");
            }
            return text.ToString();
        }

        /// <summary>The findings as GeoJSON: each one a Feature whose geometry is its cell.</summary>
        public static string ToGeoJson(ResultsDocument results)
        {
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteString("type", "FeatureCollection");
                json.WriteString("name", FileName(results));
                json.WriteStartObject("vigil");
                json.WriteString("contract_version", results.ContractVersion);
                if (results.Run == null) json.WriteNull("run_id"); else json.WriteString("run_id", results.Run.Id);
                json.WriteBoolean("synthetic", results.Synthetic);
                if (results.Run != null)
                {
                    json.WriteStartObject("period");
                    json.WriteString("start", Month(results.Run.Settings.Period.Start));
                    json.WriteString("end", Month(results.Run.Settings.Period.End));
                    json.WriteEndObject();
                }
                json.WriteEndObject();

                json.WriteStartArray("features");
                foreach (var finding in results.Findings.OrderBy(finding => finding.Rank))
                {
                    var cell = finding.Cell;
                    json.WriteStartObject();
                    json.WriteString("type", "Feature");
                    json.WriteString("id", finding.Id);
                    json.WriteStartObject("geometry");
                    json.WriteString("type", "Polygon");
                    json.WriteStartArray("coordinates");
                    json.WriteStartArray();
                    // One closed ring, counter-clockwise from the south-west corner, as longitude then latitude.
                    foreach (var (longitude, latitude) in new[]
                    {
                        (cell.LonMin, cell.LatMin), (cell.LonMax, cell.LatMin), (cell.LonMax, cell.LatMax), (cell.LonMin, cell.LatMax), (cell.LonMin, cell.LatMin)
                    })
                    {
                        json.WriteStartArray();
                        json.WriteNumberValue(longitude);
                        json.WriteNumberValue(latitude);
                        json.WriteEndArray();
                    }
                    json.WriteEndArray();
                    json.WriteEndArray();
                    json.WriteEndObject();

                    json.WriteStartObject("properties");
                    var row = Row(finding, results);
                    for (int i = 0; i < Columns.Count; i++)
                    {
                        switch (row[i])
                        {
                            case null: json.WriteNull(Columns[i]); break;
                            case bool flag: json.WriteBoolean(Columns[i], flag); break;
                            case int whole: json.WriteNumber(Columns[i], whole); break;
                            case double number when double.IsFinite(number): json.WriteNumber(Columns[i], number); break;
                            case double: json.WriteNull(Columns[i]); break;
                            default: json.WriteString(Columns[i], row[i]!.ToString()); break;
                        }
                    }
                    json.WriteEndObject();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string Month(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>One CSV field: empty for nothing, quoted when it holds a comma, a quote or a line break.</summary>
        private static string Field(object? value)
        {
            string text = value switch
            {
                null => "",
                bool flag => flag ? "true" : "false",
                double number => double.IsFinite(number) ? number.ToString("R", CultureInfo.InvariantCulture) : "",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
            return text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
        }
    }
}
