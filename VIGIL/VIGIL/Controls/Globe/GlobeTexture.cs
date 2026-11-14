using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  Equirectangular image held as packed 32-bit pixels, with a box-filtered mip chain so the
    ///  renderer can pick a level close to one texel per screen pixel and sample it bilinearly
    ///  without shimmer. Columns wrap across the dateline and rows clamp at the poles.
    ///  Each level can carry an optional single-channel map (0 to 255 per texel), stored in the
    ///  alpha byte, which the opaque imagery doesn't otherwise need. The day texture uses it for
    ///  the water mask that drives the ocean glint.
    /// </summary>
    public sealed class GlobeTexture
    {
        /// <summary>One resolution of the mip chain. Level 0 is the full image.</summary>
        public sealed class Level
        {
            public Level(int width, int height, int[] pixels, bool hasChannel)
            {
                Width = width;
                Height = height;
                Pixels = pixels;
                HasChannel = hasChannel;
            }

            public int Width { get; }
            public int Height { get; }

            /// <summary>Packed texels: red, green and blue, with the channel map (or 255) in the top byte.</summary>
            public int[] Pixels { get; }
            public bool HasChannel { get; }
        }

        private readonly Level[] levels;

        private GlobeTexture(Level[] levels)
        {
            this.levels = levels;
        }

        public IReadOnlyList<Level> Levels => levels;
        public int Width => levels[0].Width;
        public int Height => levels[0].Height;

        /// <summary>
        ///  Copies a bitmap into memory and builds mip levels down to roughly 256 px wide.
        ///  <paramref name="channel"/> derives the optional per-texel channel from a packed pixel.
        /// </summary>
        public static GlobeTexture FromBitmap(Bitmap bitmap, Func<int, byte>? channel = null)
        {
            int width = bitmap.Width;
            int height = bitmap.Height;
            var pixels = new int[width * height];

            var bounds = new Rectangle(0, 0, width, height);
            var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * width, width);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            if (channel != null)
            {
                Parallel.For(0, height, y =>
                {
                    int row = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        int pixel = pixels[row + x];
                        pixels[row + x] = (channel(pixel) << 24) | (pixel & 0xFFFFFF);
                    }
                });
            }

            var chain = new List<Level> { new(width, height, pixels, channel != null) };
            while (chain[^1].Width > 256)
            {
                chain.Add(Downsample(chain[^1]));
            }
            return new GlobeTexture(chain.ToArray());
        }

        /// <summary>
        ///  The finest level whose texel density stays at or below <paramref name="maxTexelsPerPixel"/>
        ///  when one screen pixel covers <paramref name="degreesPerPixel"/> degrees of longitude.
        /// </summary>
        public Level LevelFor(float degreesPerPixel, float maxTexelsPerPixel)
        {
            for (int i = 0; i < levels.Length - 1; i++)
            {
                float texelsPerPixel = levels[i].Width / 360f * degreesPerPixel;
                if (texelsPerPixel <= maxTexelsPerPixel)
                {
                    return levels[i];
                }
            }
            return levels[^1];
        }

        /// <summary>
        ///  Bilinear sample at texture coordinates in [0, 1]. Colour channels come back in 0 to 255;
        ///  <paramref name="channel"/> is the optional map in 0 to 1 (0 if the level has none).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Sample(Level level, float u, float v, out float r, out float g, out float b, out float channel)
        {
            int w = level.Width;
            int h = level.Height;
            float x = u * w - 0.5f;
            float y = v * h - 0.5f;

            // Truncation after an offset acts as floor for the small negative values at the seam.
            int x0 = (int)(x + w) - w;
            int y0 = (int)(y + 1f) - 1;
            float fx = x - x0;
            float fy = y - y0;

            int x1 = x0 + 1;
            if (x0 < 0) x0 += w;
            if (x1 >= w) x1 -= w;
            int y1 = y0 + 1;
            if (y0 < 0) y0 = 0;
            if (y1 >= h) y1 = h - 1;

            int i00 = y0 * w + x0;
            int i10 = y0 * w + x1;
            int i01 = y1 * w + x0;
            int i11 = y1 * w + x1;

            float w00 = (1f - fx) * (1f - fy);
            float w10 = fx * (1f - fy);
            float w01 = (1f - fx) * fy;
            float w11 = fx * fy;

            int[] px = level.Pixels;
            int p00 = px[i00], p10 = px[i10], p01 = px[i01], p11 = px[i11];
            r = ((p00 >> 16) & 255) * w00 + ((p10 >> 16) & 255) * w10 + ((p01 >> 16) & 255) * w01 + ((p11 >> 16) & 255) * w11;
            g = ((p00 >> 8) & 255) * w00 + ((p10 >> 8) & 255) * w10 + ((p01 >> 8) & 255) * w01 + ((p11 >> 8) & 255) * w11;
            b = (p00 & 255) * w00 + (p10 & 255) * w10 + (p01 & 255) * w01 + (p11 & 255) * w11;

            channel = level.HasChannel
                ? (((p00 >> 24) & 255) * w00 + ((p10 >> 24) & 255) * w10 + ((p01 >> 24) & 255) * w01 + ((p11 >> 24) & 255) * w11) * (1f / 255f)
                : 0f;
        }

        /// <summary>Averages 2x2 blocks into the next level. Odd edges reuse the last row or column.</summary>
        private static Level Downsample(Level source)
        {
            int sw = source.Width;
            int sh = source.Height;
            int w = Math.Max(1, sw / 2);
            int h = Math.Max(1, sh / 2);
            var pixels = new int[w * h];
            bool hasChannel = source.HasChannel;

            Parallel.For(0, h, y =>
            {
                int sy0 = Math.Min(2 * y, sh - 1);
                int sy1 = Math.Min(2 * y + 1, sh - 1);
                int[] src = source.Pixels;
                for (int x = 0; x < w; x++)
                {
                    int sx0 = Math.Min(2 * x, sw - 1);
                    int sx1 = Math.Min(2 * x + 1, sw - 1);
                    int a = src[sy0 * sw + sx0], b = src[sy0 * sw + sx1], c = src[sy1 * sw + sx0], d = src[sy1 * sw + sx1];
                    int r = (((a >> 16) & 255) + ((b >> 16) & 255) + ((c >> 16) & 255) + ((d >> 16) & 255) + 2) >> 2;
                    int g = (((a >> 8) & 255) + ((b >> 8) & 255) + ((c >> 8) & 255) + ((d >> 8) & 255) + 2) >> 2;
                    int bl = ((a & 255) + (b & 255) + (c & 255) + (d & 255) + 2) >> 2;
                    int top = hasChannel
                        ? (((a >> 24) & 255) + ((b >> 24) & 255) + ((c >> 24) & 255) + ((d >> 24) & 255) + 2) >> 2
                        : 255;
                    pixels[y * w + x] = (top << 24) | (r << 16) | (g << 8) | bl;
                }
            });

            return new Level(w, h, pixels, hasChannel);
        }
    }
}
