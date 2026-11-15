using System.Globalization;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>
    ///  Web addresses that open NASA Worldview on a finding's cell, so a person can look at the
    ///  place itself in satellite imagery. The address format is Worldview's own, from its
    ///  documentation of URL parameters: <c>p</c> the projection, <c>v</c> the view as
    ///  west, south, east, north in degrees, and <c>t</c> the day. Nothing is fetched here.
    /// </summary>
    public static class Worldview
    {
        /// <summary>Degrees of surroundings shown round the cell, so it can be placed by eye.</summary>
        private const double Margin = 5;

        /// <summary>
        ///  Worldview on the finding's cell, on the middle day of the month that matters most to
        ///  it: the month a change point puts the change in, and otherwise the last month of the period.
        /// </summary>
        public static string Link(Finding finding)
        {
            var cell = finding.Cell;
            var month = finding.ChangePoint?.Date ?? finding.Period.End;
            var day = new DateOnly(month.Year, month.Month, 15);
            string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
            string view = string.Join(",",
                Number(Math.Max(-180, cell.LonMin - Margin)), Number(Math.Max(-90, cell.LatMin - Margin)),
                Number(Math.Min(180, cell.LonMax + Margin)), Number(Math.Min(90, cell.LatMax + Margin)));
            return "https://worldview.earthdata.nasa.gov/?p=geographic&v=" + view
                + "&t=" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "-T12:00:00Z";
        }
    }
}
