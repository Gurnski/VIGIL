using System.Runtime.CompilerServices;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  A lat/lon grid of values (for example a trend per cell) ready for the renderer: values
    ///  flattened to floats, NaN for missing cells, and a colour lookup whose alpha carries the
    ///  scale's own fade. Rows run south to north from LatitudeMin, columns west to east from
    ///  LongitudeMin. Overall opacity is applied by the renderer so it can change without a rebuild.
    /// </summary>
    internal sealed class GridOverlay
    {
        private const int LookupSize = 256;

        private readonly float[] values;
        private readonly int rows;
        private readonly int columns;
        private readonly float latitudeMin;
        private readonly float latitudeSpan;
        private readonly float longitudeMin;
        private readonly float longitudeSpan;
        private readonly bool wrapsAround;
        private readonly float minimum;
        private readonly float range;
        private readonly int[] lookup;
        private readonly bool smooth;

        public GridOverlay(double[,] source, double latitudeMin, double latitudeMax, double longitudeMin, double longitudeMax,
            ColourScale scale, double minimum, double maximum, bool smooth)
        {
            rows = source.GetLength(0);
            columns = source.GetLength(1);
            values = new float[rows * columns];
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    values[r * columns + c] = (float)source[r, c];
                }
            }

            this.latitudeMin = (float)Math.Min(latitudeMin, latitudeMax);
            latitudeSpan = (float)Math.Abs(latitudeMax - latitudeMin);
            this.longitudeMin = (float)longitudeMin;
            double span = longitudeMax - longitudeMin;
            if (span <= 0) span += 360;
            longitudeSpan = (float)Math.Min(span, 360);
            wrapsAround = longitudeSpan >= 359.999f;

            this.minimum = (float)minimum;
            range = (float)Math.Max(maximum - minimum, 1e-9);
            this.smooth = smooth;
            Scale = scale;
            Minimum = minimum;
            Maximum = maximum;
            lookup = scale.BuildLookup(LookupSize);
        }

        public ColourScale Scale { get; }
        public double Minimum { get; }
        public double Maximum { get; }

        /// <summary>Packed ARGB for a point, or 0 where the grid has no data.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Sample(float latitude, float longitude)
        {
            float fy = (latitude - latitudeMin) / latitudeSpan;
            if (fy < 0f || fy > 1f)
            {
                return 0;
            }

            float x = longitude - longitudeMin;
            x -= 360f * MathF.Floor(x / 360f);
            float fx = x / longitudeSpan;
            if (fx > 1f)
            {
                return 0;
            }

            if (!smooth)
            {
                int row = Math.Min((int)(fy * rows), rows - 1);
                int column = Math.Min((int)(fx * columns), columns - 1);
                return Colour(values[row * columns + column]);
            }
            return SampleSmooth(fx, fy);
        }

        /// <summary>Bilinear blend of the four nearest cell centres, ignoring cells with no data.</summary>
        private int SampleSmooth(float fx, float fy)
        {
            float gx = fx * columns - 0.5f;
            float gy = fy * rows - 0.5f;
            int c0 = (int)MathF.Floor(gx);
            int r0 = (int)MathF.Floor(gy);
            float tx = gx - c0;
            float ty = gy - r0;
            int c1 = c0 + 1;
            int r1 = Math.Min(r0 + 1, rows - 1);
            r0 = Math.Max(r0, 0);

            if (wrapsAround)
            {
                if (c0 < 0) c0 += columns;
                if (c1 >= columns) c1 -= columns;
            }
            else
            {
                c0 = Math.Max(c0, 0);
                c1 = Math.Min(c1, columns - 1);
            }

            float sum = 0f, weight = 0f;
            Accumulate(values[r0 * columns + c0], (1f - tx) * (1f - ty), ref sum, ref weight);
            Accumulate(values[r0 * columns + c1], tx * (1f - ty), ref sum, ref weight);
            Accumulate(values[r1 * columns + c0], (1f - tx) * ty, ref sum, ref weight);
            Accumulate(values[r1 * columns + c1], tx * ty, ref sum, ref weight);
            return weight < 0.25f ? 0 : Colour(sum / weight);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Accumulate(float value, float w, ref float sum, ref float weight)
        {
            if (!float.IsNaN(value))
            {
                sum += value * w;
                weight += w;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Colour(float value)
        {
            if (float.IsNaN(value))
            {
                return 0;
            }
            int index = (int)((value - minimum) / range * (LookupSize - 1) + 0.5f);
            if (index < 0) index = 0;
            else if (index >= LookupSize) index = LookupSize - 1;
            return lookup[index];
        }
    }
}
