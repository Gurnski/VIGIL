using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>
    ///  The geometry of a drawn zone: which points and which grid cells are inside its outline.
    ///  This is layout, not statistics. Anything measured over a zone comes from the Python service.
    ///
    ///  The rule, which the service follows too: an outline's edges are straight lines in
    ///  latitude and longitude, it is closed from its last point to its first, and a grid cell
    ///  belongs to the zone when the cell's centre is inside. Longitudes along an outline are
    ///  continuous, so one that crosses the date line runs past ±180 instead of jumping.
    /// </summary>
    public static class ZoneGeometry
    {
        /// <summary>The most points a saved outline keeps. A drawn stroke is thinned to this.</summary>
        public const int MaxPoints = 240;

        /// <summary>
        ///  Makes the longitudes of a path continuous: each point's longitude is moved by whole
        ///  turns so that it is the short way round from the point before.
        /// </summary>
        public static List<(double Latitude, double Longitude)> Unwrap(IEnumerable<(double Latitude, double Longitude)> path)
        {
            var result = new List<(double Latitude, double Longitude)>();
            foreach (var (latitude, longitude) in path)
            {
                double continuous = result.Count == 0 ? longitude : result[^1].Longitude + ShortWay(longitude - result[^1].Longitude);
                result.Add((latitude, continuous));
            }
            return result;
        }

        /// <summary>A change in longitude folded into -180 to 180.</summary>
        public static double ShortWay(double delta)
        {
            delta = (delta + 180) % 360;
            if (delta < 0)
            {
                delta += 360;
            }
            return delta - 180;
        }

        /// <summary>
        ///  True when a closed outline goes all the way round a pole: its last point ends up a
        ///  whole turn of longitude from its first. Such an outline has no inside in latitude
        ///  and longitude, so it cannot be a zone.
        /// </summary>
        public static bool WrapsPole(IReadOnlyList<(double Latitude, double Longitude)> outline)
        {
            if (outline.Count < 3)
            {
                return false;
            }
            double west = outline.Min(point => point.Longitude);
            double east = outline.Max(point => point.Longitude);
            return Math.Abs(outline[^1].Longitude - outline[0].Longitude) > 180 || east - west >= 360;
        }

        /// <summary>Whether a point is inside a closed outline, by the even-odd rule.</summary>
        public static bool Contains(IReadOnlyList<(double Latitude, double Longitude)> outline, double latitude, double longitude)
        {
            if (outline.Count < 3)
            {
                return false;
            }

            double west = double.PositiveInfinity, east = double.NegativeInfinity;
            foreach (var point in outline)
            {
                west = Math.Min(west, point.Longitude);
                east = Math.Max(east, point.Longitude);
            }
            // The outline's longitudes may sit outside ±180, so bring the point into the same turn.
            double lon = longitude + 360 * Math.Ceiling((west - longitude) / 360);
            if (lon > east)
            {
                return false;
            }

            bool inside = false;
            for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
            {
                var a = outline[i];
                var b = outline[j];
                if ((a.Latitude > latitude) != (b.Latitude > latitude)
                    && lon < (b.Longitude - a.Longitude) * (latitude - a.Latitude) / (b.Latitude - a.Latitude) + a.Longitude)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>The grid cells whose centres are inside the outline, as rows and columns of the scan grid.</summary>
        public static List<(int Row, int Column)> Cells(IReadOnlyList<(double Latitude, double Longitude)> outline, GridSettings grid)
        {
            var cells = new List<(int Row, int Column)>();
            if (outline.Count < 3 || grid.CellDegrees <= 0)
            {
                return cells;
            }

            double south = outline.Min(point => point.Latitude);
            double north = outline.Max(point => point.Latitude);
            for (int row = 0; row < grid.Rows; row++)
            {
                double latitude = grid.LatMin + (row + 0.5) * grid.CellDegrees;
                if (latitude < south || latitude > north)
                {
                    continue;
                }
                for (int column = 0; column < grid.Columns; column++)
                {
                    if (Contains(outline, latitude, grid.LonMin + (column + 0.5) * grid.CellDegrees))
                    {
                        cells.Add((row, column));
                    }
                }
            }
            return cells;
        }

        /// <summary>The contract's id for a cell, such as r60c112.</summary>
        public static string CellId(int row, int column) =>
            "r" + row.ToString("00", System.Globalization.CultureInfo.InvariantCulture)
            + "c" + column.ToString("000", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        ///  The sides of the cells that face a cell outside the set: the stepped edge of the
        ///  area a zone really covers, for drawing beside the outline the user drew.
        /// </summary>
        public static List<((double Latitude, double Longitude) From, (double Latitude, double Longitude) To)> BoundaryEdges(
            IReadOnlyCollection<(int Row, int Column)> cells, GridSettings grid)
        {
            var edges = new List<((double, double), (double, double))>();
            var members = cells as HashSet<(int Row, int Column)> ?? new HashSet<(int Row, int Column)>(cells);
            bool wraps = Math.Abs(grid.Columns * grid.CellDegrees - 360) < 1e-6;
            double size = grid.CellDegrees;

            bool Has(int row, int column)
            {
                if (wraps)
                {
                    column = (column % grid.Columns + grid.Columns) % grid.Columns;
                }
                return members.Contains((row, column));
            }

            foreach (var (row, column) in members)
            {
                double south = grid.LatMin + row * size;
                double west = grid.LonMin + column * size;
                if (!Has(row + 1, column)) edges.Add(((south + size, west), (south + size, west + size)));
                if (!Has(row - 1, column)) edges.Add(((south, west), (south, west + size)));
                if (!Has(row, column - 1)) edges.Add(((south, west), (south + size, west)));
                if (!Has(row, column + 1)) edges.Add(((south, west + size), (south + size, west + size)));
            }
            return edges;
        }

        /// <summary>
        ///  Thins a drawn path to at most <see cref="MaxPoints"/> points, keeping its shape:
        ///  a point is dropped when it lies within the tolerance of the line between its
        ///  neighbours that are kept (Douglas-Peucker). The tolerance starts well below a grid
        ///  cell and doubles until the path is short enough.
        /// </summary>
        public static List<(double Latitude, double Longitude)> Simplify(IReadOnlyList<(double Latitude, double Longitude)> path, double toleranceDegrees = 0.05)
        {
            var result = path.ToList();
            double tolerance = toleranceDegrees;
            while (true)
            {
                result = SimplifyOnce(path, tolerance);
                if (result.Count <= MaxPoints || tolerance > 5)
                {
                    return result;
                }
                tolerance *= 2;
            }
        }

        private static List<(double Latitude, double Longitude)> SimplifyOnce(IReadOnlyList<(double Latitude, double Longitude)> path, double tolerance)
        {
            if (path.Count < 3)
            {
                return path.ToList();
            }

            var keep = new bool[path.Count];
            keep[0] = keep[^1] = true;
            var spans = new Stack<(int First, int Last)>();
            spans.Push((0, path.Count - 1));
            while (spans.Count > 0)
            {
                var (first, last) = spans.Pop();
                double furthest = 0;
                int index = -1;
                for (int i = first + 1; i < last; i++)
                {
                    double distance = DistanceToSegment(path[i], path[first], path[last]);
                    if (distance > furthest)
                    {
                        furthest = distance;
                        index = i;
                    }
                }
                if (index >= 0 && furthest > tolerance)
                {
                    keep[index] = true;
                    spans.Push((first, index));
                    spans.Push((index, last));
                }
            }

            var result = new List<(double Latitude, double Longitude)>();
            for (int i = 0; i < path.Count; i++)
            {
                if (keep[i])
                {
                    result.Add(path[i]);
                }
            }
            return result;
        }

        private static double DistanceToSegment((double Latitude, double Longitude) point,
            (double Latitude, double Longitude) from, (double Latitude, double Longitude) to)
        {
            double dx = to.Longitude - from.Longitude;
            double dy = to.Latitude - from.Latitude;
            double lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared <= 0 ? 0 : Math.Clamp(((point.Longitude - from.Longitude) * dx + (point.Latitude - from.Latitude) * dy) / lengthSquared, 0, 1);
            double x = from.Longitude + t * dx - point.Longitude;
            double y = from.Latitude + t * dy - point.Latitude;
            return Math.Sqrt(x * x + y * y);
        }
    }
}
