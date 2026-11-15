using System.Drawing.Drawing2D;
using System.Numerics;
using VIGIL.Theming;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  The grid overlay drawn as columns standing on the globe: each cell with a value rises by
    ///  the size of that value against the colour scale's own range, and keeps the colour the
    ///  flat layer gives it. Height shows nothing the colour does not. It makes the size of a
    ///  change readable at a glance, and it is only a way of drawing the layer.
    ///  Columns are drawn over the rendered frame, far ones first, on the side of the planet
    ///  that faces the camera. When the surface is unrolled they stand on it wherever the fold
    ///  has put their cells, and on the flat map they stand taller, as everything off its surface does.
    /// </summary>
    internal sealed class GlobeRelief
    {
        /// <summary>A value under this share of the scale's range stays flat: a column a pixel high is only noise.</summary>
        private const float FlatBelow = 0.03f;

        private GridOverlay? builtFor;
        private Vector3[] corners = Array.Empty<Vector3>();      // (rows + 1) by (columns + 1) unit vectors
        private Vector3[] centres = Array.Empty<Vector3>();      // rows by columns unit vectors

        // The same places on the surface as it is unrolled, and which way is up from each. Worked out again whenever the fold moves.
        private Vector3[] placedCorners = Array.Empty<Vector3>();
        private Vector3[] cornerUps = Array.Empty<Vector3>();
        private Vector3[] placedCentres = Array.Empty<Vector3>();
        private Vector3[] centreUps = Array.Empty<Vector3>();
        private GridOverlay? placedFor;
        private float placedFold = -1f;
        private readonly List<Column> visible = new();
        private readonly PointF[] quad = new PointF[4];

        private struct Column
        {
            public float Depth;
            public int Row, Col;
            public float Height;
            public Color Colour;
        }

        /// <summary>How far above the surface, in planet radii, a point over this place stands. 0 where the layer is flat or empty.</summary>
        public static float HeightAt(GridOverlay grid, double latitude, double longitude, float tallest)
        {
            if (!grid.CellAt((float)latitude, (float)longitude, out int row, out int column))
            {
                return 0f;
            }
            float share = grid.Share(grid.ValueAt(row, column));
            return share < FlatBelow ? 0f : tallest * share;
        }

        public void Draw(Graphics g, GlobeCamera camera, GridOverlay grid, float tallest, Rectangle bounds)
        {
            Prepare(grid);
            int rows = grid.Rows, columns = grid.Columns, stride = columns + 1;
            var eye = camera.Eye;
            bool unrolled = camera.Fold > 0f;
            if (unrolled)
            {
                Place(grid, camera);
            }
            var ground = unrolled ? placedCorners : corners;
            var middles = unrolled ? placedCentres : centres;
            // On the flat map a column stands taller, by the same share as a marker over it does.
            float taller = 1f + (GlobeCamera.FlatHeightScale - 1f) * camera.Fold;

            visible.Clear();
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    float value = grid.ValueAt(row, column);
                    if (float.IsNaN(value))
                    {
                        continue;
                    }
                    float share = grid.Share(value);
                    if (share < FlatBelow)
                    {
                        continue;
                    }
                    var centre = middles[row * columns + column];
                    // Only the side of the planet that faces the camera: a column beyond the horizon is left out.
                    if (unrolled ? float.IsNaN(centre.X) || camera.FacingOf(centre) <= 0.004f : Vector3.Dot(eye, centre) - 1f <= 0.004f * (camera.EyeLength - 1f))
                    {
                        continue;
                    }
                    float height = tallest * share;
                    var top = unrolled ? centre + centreUps[row * columns + column] * (height * taller) : centre * (1f + height);
                    if (!OnScreen(camera, top, bounds) && !OnScreen(camera, centre, bounds))
                    {
                        continue;
                    }
                    visible.Add(new Column
                    {
                        Depth = (top - eye).LengthSquared(),
                        Row = row,
                        Col = column,
                        Height = height,
                        Colour = Opaque(grid.ColourOf(value)),
                    });
                }
            }
            if (visible.Count == 0)
            {
                return;
            }
            visible.Sort((a, b) => b.Depth.CompareTo(a.Depth));

            var smoothing = g.SmoothingMode;
            // Thousands of small faces: edges are left hard, which also keeps neighbours from showing seams between them.
            g.SmoothingMode = visible.Count > 600 ? SmoothingMode.None : SmoothingMode.AntiAlias;
            Span<Vector3> low = stackalloc Vector3[4];
            Span<Vector3> high = stackalloc Vector3[4];
            foreach (var item in visible)
            {
                int at = item.Row * stride + item.Col;
                low[0] = ground[at];                     // south-west
                low[1] = ground[at + 1];                 // south-east
                low[2] = ground[at + stride + 1];        // north-east
                low[3] = ground[at + stride];            // north-west
                if (unrolled)
                {
                    float rise = item.Height * taller;
                    high[0] = low[0] + cornerUps[at] * rise;
                    high[1] = low[1] + cornerUps[at + 1] * rise;
                    high[2] = low[2] + cornerUps[at + stride + 1] * rise;
                    high[3] = low[3] + cornerUps[at + stride] * rise;
                }
                else
                {
                    float lift = 1f + item.Height;
                    for (int i = 0; i < 4; i++)
                    {
                        high[i] = low[i] * lift;
                    }
                }
                var middle = middles[item.Row * columns + item.Col];

                for (int side = 0; side < 4; side++)
                {
                    int next = (side + 1) & 3;
                    var faceMiddle = (low[side] + low[next]) * 0.5f;
                    var outward = faceMiddle - middle;
                    if (Vector3.Dot(outward, eye - faceMiddle) <= 0f)
                    {
                        continue;                        // this face looks away from the camera
                    }
                    if (Project(camera, low[side], out quad[0]) && Project(camera, low[next], out quad[1])
                        && Project(camera, high[next], out quad[2]) && Project(camera, high[side], out quad[3]))
                    {
                        // Two flat shades, by which way the face looks, so a column reads as a solid. No gradient.
                        using var shade = new SolidBrush(Shade(item.Colour, (side & 1) == 0 ? 0.58f : 0.74f));
                        g.FillPolygon(shade, quad);
                    }
                }
                if (Project(camera, high[0], out quad[0]) && Project(camera, high[1], out quad[1])
                    && Project(camera, high[2], out quad[2]) && Project(camera, high[3], out quad[3]))
                {
                    using var lid = new SolidBrush(item.Colour);
                    g.FillPolygon(lid, quad);
                }
            }
            g.SmoothingMode = smoothing;
        }

        private void Prepare(GridOverlay grid)
        {
            if (ReferenceEquals(grid, builtFor))
            {
                return;
            }
            int rows = grid.Rows, columns = grid.Columns;
            corners = new Vector3[(rows + 1) * (columns + 1)];
            centres = new Vector3[rows * columns];
            double latitudeStep = grid.LatitudeSpan / rows, longitudeStep = grid.LongitudeSpan / columns;
            for (int row = 0; row <= rows; row++)
            {
                for (int column = 0; column <= columns; column++)
                {
                    corners[row * (columns + 1) + column] = GlobeCamera.ToUnit(grid.LatitudeMin + row * latitudeStep, grid.LongitudeMin + column * longitudeStep);
                    if (row < rows && column < columns)
                    {
                        centres[row * columns + column] = GlobeCamera.ToUnit(grid.LatitudeMin + (row + 0.5) * latitudeStep, grid.LongitudeMin + (column + 0.5) * longitudeStep);
                    }
                }
            }
            builtFor = grid;
        }

        /// <summary>
        ///  Puts every corner and centre where the fold has it, with the way up from each. A
        ///  cell that would straddle 180° has one side at each edge of the unrolled surface, so
        ///  its centre is marked as nowhere and its column is left out.
        /// </summary>
        private void Place(GridOverlay grid, GlobeCamera camera)
        {
            if (ReferenceEquals(grid, placedFor) && camera.Fold == placedFold)
            {
                return;
            }
            int rows = grid.Rows, columns = grid.Columns;
            if (placedCorners.Length != corners.Length || placedCentres.Length != centres.Length)
            {
                placedCorners = new Vector3[corners.Length];
                cornerUps = new Vector3[corners.Length];
                placedCentres = new Vector3[centres.Length];
                centreUps = new Vector3[centres.Length];
            }
            double latitudeStep = grid.LatitudeSpan / rows, longitudeStep = grid.LongitudeSpan / columns;
            for (int row = 0; row <= rows; row++)
            {
                for (int column = 0; column <= columns; column++)
                {
                    double latitude = grid.LatitudeMin + row * latitudeStep, longitude = grid.LongitudeMin + column * longitudeStep;
                    int corner = row * (columns + 1) + column;
                    placedCorners[corner] = camera.Place(latitude, longitude);
                    cornerUps[corner] = camera.UpAt(latitude, longitude);
                    if (row < rows && column < columns)
                    {
                        int cell = row * columns + column;
                        double east = longitude + longitudeStep;
                        if (Math.Abs(GlobeCamera.WrapLongitude(longitude) - GlobeCamera.WrapLongitude(east)) > 180)
                        {
                            placedCentres[cell] = new Vector3(float.NaN);
                            continue;
                        }
                        placedCentres[cell] = camera.Place(latitude + latitudeStep / 2, longitude + longitudeStep / 2);
                        centreUps[cell] = camera.UpAt(latitude + latitudeStep / 2, longitude + longitudeStep / 2);
                    }
                }
            }
            placedFor = grid;
            placedFold = camera.Fold;
        }

        private static bool Project(GlobeCamera camera, Vector3 point, out PointF screen)
        {
            var v = point - camera.Eye;
            float z = Vector3.Dot(v, camera.Forward);
            if (z <= 1e-4f)
            {
                screen = PointF.Empty;
                return false;
            }
            screen = new PointF(camera.CentreX + camera.Focal * Vector3.Dot(v, camera.Right) / z, camera.CentreY - camera.Focal * Vector3.Dot(v, camera.Up) / z);
            return true;
        }

        private static bool OnScreen(GlobeCamera camera, Vector3 point, Rectangle bounds)
        {
            const int margin = 80;
            return Project(camera, point, out var screen)
                && screen.X > bounds.Left - margin && screen.X < bounds.Right + margin
                && screen.Y > bounds.Top - margin && screen.Y < bounds.Bottom + margin;
        }

        /// <summary>The layer's colour made solid: its own fade towards zero is kept by mixing it into the surface tone.</summary>
        private static Color Opaque(int argb)
        {
            float alpha = ((argb >> 24) & 0xFF) / 255f;
            var surface = Theme.SurfaceRaised;
            int red = (int)(((argb >> 16) & 0xFF) * alpha + surface.R * (1f - alpha));
            int green = (int)(((argb >> 8) & 0xFF) * alpha + surface.G * (1f - alpha));
            int blue = (int)((argb & 0xFF) * alpha + surface.B * (1f - alpha));
            return Color.FromArgb(255, red, green, blue);
        }

        private static Color Shade(Color colour, float light) =>
            Color.FromArgb(255, (int)(colour.R * light), (int)(colour.G * light), (int)(colour.B * light));
    }
}
