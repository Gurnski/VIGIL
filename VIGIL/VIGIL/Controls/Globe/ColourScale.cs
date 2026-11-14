using VIGIL.Theming;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  Maps a value in [0, 1] to a colour through evenly spaced stops. Shared by the globe's
    ///  gridded overlay and the legend that explains it, so both show the same colours.
    /// </summary>
    public sealed class ColourScale
    {
        private readonly Color[] stops;

        public ColourScale(IReadOnlyList<Color> stops, bool diverging, string name)
        {
            if (stops.Count < 2)
            {
                throw new ArgumentException("A colour scale needs at least two stops.", nameof(stops));
            }
            this.stops = stops.ToArray();
            Diverging = diverging;
            Name = name;
        }

        /// <summary>
        ///  Blue, white, red: ColorBrewer's RdBu scheme reversed (Cynthia Brewer, colorbrewer2.org,
        ///  Apache 2.0). Colour-vision safe, with the neutral centre for "no change".
        /// </summary>
        public static ColourScale BlueWhiteRed { get; } = new(new[]
        {
            Color.FromArgb(0x05, 0x30, 0x61),
            Color.FromArgb(0x21, 0x66, 0xAC),
            Color.FromArgb(0x43, 0x93, 0xC3),
            Color.FromArgb(0x92, 0xC5, 0xDE),
            Color.FromArgb(0xD1, 0xE5, 0xF0),
            Color.FromArgb(0xF7, 0xF7, 0xF7),
            Color.FromArgb(0xFD, 0xDB, 0xC7),
            Color.FromArgb(0xF4, 0xA5, 0x82),
            Color.FromArgb(0xD6, 0x60, 0x4D),
            Color.FromArgb(0xB2, 0x18, 0x2B),
            Color.FromArgb(0x67, 0x00, 0x1F)
        }, diverging: true, "Blue-white-red");

        /// <summary>Dark to accent to white, built from the theme, for one-signed quantities.</summary>
        public static ColourScale Sequential { get; } = new(new[]
        {
            Theme.SurfaceRaised, Theme.AccentPressed, Theme.Accent, Theme.AccentHover, Theme.TextPrimary
        }, diverging: false, "Sequential");

        public string Name { get; }

        /// <summary>
        ///  True when the middle of the scale means "no change". The overlay then fades those
        ///  cells towards transparent so the eye goes to where things are changing.
        /// </summary>
        public bool Diverging { get; }

        public Color At(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            float position = t * (stops.Length - 1);
            int index = Math.Min((int)position, stops.Length - 2);
            return Draw.Blend(stops[index], stops[index + 1], position - index);
        }

        /// <summary>Opacity multiplier for a position on the scale, in [0, 1].</summary>
        public float OpacityAt(float t)
        {
            if (!Diverging)
            {
                return 1f;
            }
            float distance = Math.Abs(2f * Math.Clamp(t, 0f, 1f) - 1f);
            return 0.18f + 0.82f * distance;
        }

        /// <summary>Packed ARGB lookup table with the opacity folded into the alpha channel.</summary>
        public int[] BuildLookup(int size)
        {
            var table = new int[size];
            for (int i = 0; i < size; i++)
            {
                float t = i / (float)(size - 1);
                var colour = At(t);
                int alpha = (int)Math.Round(255f * OpacityAt(t));
                table[i] = (alpha << 24) | (colour.R << 16) | (colour.G << 8) | colour.B;
            }
            return table;
        }
    }
}
