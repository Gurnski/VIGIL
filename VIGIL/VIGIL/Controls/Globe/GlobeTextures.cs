namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  The globe's day and night imagery, decoded once from the embedded JPGs on a background
    ///  thread and shared by every <see cref="GlobeControl"/>.
    ///
    ///  Day: NASA Blue Marble Next Generation with topography and bathymetry, August 2004,
    ///  downscaled from the 21600x10800 original to 8192x4096 (JPEG quality 88) for the repository.
    ///  https://eoimages.gsfc.nasa.gov/images/imagerecords/73000/73776/world.topo.bathy.200408.3x21600x10800.jpg
    ///  Credit: NASA Earth Observatory / Visible Earth (Reto Stöckli, NASA GSFC). Public domain.
    ///
    ///  Night: NASA Black Marble 2016 (Suomi NPP VIIRS day/night band composite), downscaled from
    ///  13500x6750 to 5400x2700 for the repository.
    ///  https://eoimages.gsfc.nasa.gov/images/imagerecords/144000/144898/BlackMarble_2016_3km.jpg
    ///  Credit: NASA Earth Observatory images by Joshua Stevens, using Suomi NPP VIIRS data from
    ///  Miguel Román, NASA GSFC. Public domain.
    /// </summary>
    public sealed class GlobeTextures
    {
        public const string DayResource = "VIGIL.Assets.BlueMarbleDay.jpg";
        public const string NightResource = "VIGIL.Assets.BlackMarbleNight.jpg";

        private static Task<GlobeTextures>? loading;

        private GlobeTextures(GlobeTexture day, GlobeTexture? night)
        {
            Day = day;
            Night = night;
        }

        /// <summary>Daylight imagery. Its channel map is the water mask used for the ocean glint.</summary>
        public GlobeTexture Day { get; }

        /// <summary>City lights and faint moonlit land, or null if the night image is missing.</summary>
        public GlobeTexture? Night { get; }

        /// <summary>
        ///  Starts decoding on first use and hands every later caller the same task. Decoding
        ///  5400x2700 JPGs and building their mip chains takes about a second.
        /// </summary>
        public static Task<GlobeTextures> LoadAsync()
        {
            return loading ??= Task.Run(Load);
        }

        private static GlobeTextures Load()
        {
            var day = LoadTexture(DayResource, WaterFromPixel)
                ?? throw new FileNotFoundException($"Embedded resource {DayResource} is missing.");
            var night = LoadTexture(NightResource, null);
            return new GlobeTextures(day, night);
        }

        private static GlobeTexture? LoadTexture(string resource, Func<int, byte>? channel)
        {
            using var stream = typeof(GlobeTextures).Assembly.GetManifestResourceStream(resource);
            if (stream == null)
            {
                return null;
            }
            using var bitmap = new Bitmap(stream);
            return GlobeTexture.FromBitmap(bitmap, channel);
        }

        /// <summary>
        ///  Water mask from colour alone: the Blue Marble oceans and lakes are blue-dominant at
        ///  every depth, while land, ice and cloud are not. Ratios rather than offsets so the
        ///  nearly black deep ocean still counts.
        /// </summary>
        private static byte WaterFromPixel(int pixel)
        {
            int r = (pixel >> 16) & 255;
            int g = (pixel >> 8) & 255;
            int b = pixel & 255;
            float blueOverRed = Math.Clamp((b - 1.25f * r - 4f) / 12f, 0f, 1f);
            float blueOverGreen = Math.Clamp((b - g + 6f) / 10f, 0f, 1f);
            return (byte)Math.Round(255f * blueOverRed * blueOverGreen);
        }
    }
}
