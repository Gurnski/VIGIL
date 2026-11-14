using System.Diagnostics;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VIGIL.Controls.Globe
{
    /// <summary>
    ///  Software ray-caster for the globe. Every pixel fires a ray from the camera's eye at a
    ///  unit sphere: hits are textured from the day and night imagery and lit from the sun
    ///  direction, misses get the atmospheric halo over a star field. Rows are shaded in
    ///  parallel into an int buffer that is then copied into a bitmap for GDI+ to draw.
    ///  No GPU, no unsafe code.
    /// </summary>
    internal sealed class GlobeRenderer : IDisposable
    {
        /// <summary>Everything one frame needs. The control fills it in before each render.</summary>
        public sealed class Scene
        {
            public double CentreLatitude { get; set; }
            public double CentreLongitude { get; set; }
            public double Zoom { get; set; } = 1;
            public double Heading { get; set; }
            public double Tilt { get; set; }
            public Vector3 SunDirection { get; set; } = Vector3.UnitX;
            public GlobeTextures? Textures { get; set; }
            public GridOverlay? Grid { get; set; }
            public float GridOpacity { get; set; } = 1f;
            public RegionFill[] Regions { get; set; } = Array.Empty<RegionFill>();
            public Color Background { get; set; } = Color.Black;
            public Color FallbackOcean { get; set; } = Color.MidnightBlue;
            public bool ShowStars { get; set; } = true;
        }

        /// <summary>A region's box and fill colour, flattened for the pixel loop.</summary>
        public readonly struct RegionFill
        {
            public RegionFill(GlobeRegion region)
            {
                LatitudeMin = (float)region.LatitudeMin;
                LatitudeMax = (float)region.LatitudeMax;
                LongitudeMin = (float)region.LongitudeMin;
                LongitudeSpan = (float)region.LongitudeSpan;
                R = region.Colour.R;
                G = region.Colour.G;
                B = region.Colour.B;
                Alpha = Math.Clamp(region.FillOpacity, 0f, 1f);
            }

            public readonly float LatitudeMin, LatitudeMax, LongitudeMin, LongitudeSpan;
            public readonly float R, G, B, Alpha;
        }

        /// <summary>Per-frame constants in world space, copied once so the row loop reads locals.</summary>
        private struct Frame
        {
            public float Ex, Ey, Ez;                 // eye position
            public float Rx, Ry, Rz, Ux, Uy, Uz, Fx, Fy, Fz;   // camera basis
            public float EyeDotRight, EyeDotUp, EyeDotForward;
            public float EyeDistanceTerm;            // |eye|^2 - 1, the constant term of the ray quadratic
            public float Sx, Sy, Sz;                 // sun direction
            public GlobeTexture.Level? Day, Night;
            public GridOverlay? Grid;
            public float GridOpacity;
            public RegionFill[] Regions;
            public float FallbackR, FallbackG, FallbackB;
        }

        // The terminator is a soft band around the sun's horizon rather than a hard line.
        private const float TerminatorStart = -0.10f;
        private const float TerminatorEnd = 0.08f;

        // The amber twilight band, as sun elevation (the dot product with the sun direction).
        private const float TwilightCentre = 0.02f;
        private const float TwilightWidth = 0.055f;

        // How far outside the silhouette the halo reaches, in planet radii.
        private const float HaloExtent = 0.22f;

        // Mip level choice: allow slightly more than one texel per pixel before stepping down.
        private const float MaxTexelsPerPixel = 1.35f;

        private int[] buffer = Array.Empty<int>();
        private int[] stars = Array.Empty<int>();
        private Bitmap? bitmap;
        private int width;
        private int height;
        private int starsBackground;
        private bool starsVisible;

        /// <summary>The last rendered frame, or null before the first render.</summary>
        public Bitmap? Bitmap => bitmap;

        public double LastFrameMilliseconds { get; private set; }

        public void Render(Scene scene, int targetWidth, int targetHeight)
        {
            var watch = Stopwatch.StartNew();
            EnsureBuffers(targetWidth, targetHeight, scene.Background, scene.ShowStars);

            var camera = new GlobeCamera(scene.CentreLatitude, scene.CentreLongitude, scene.Zoom, scene.Heading, scene.Tilt, width, height);
            var eye = camera.Eye;
            var frame = new Frame
            {
                Ex = eye.X, Ey = eye.Y, Ez = eye.Z,
                Rx = camera.Right.X, Ry = camera.Right.Y, Rz = camera.Right.Z,
                Ux = camera.Up.X, Uy = camera.Up.Y, Uz = camera.Up.Z,
                Fx = camera.Forward.X, Fy = camera.Forward.Y, Fz = camera.Forward.Z,
                EyeDotRight = Vector3.Dot(eye, camera.Right),
                EyeDotUp = Vector3.Dot(eye, camera.Up),
                EyeDotForward = Vector3.Dot(eye, camera.Forward),
                EyeDistanceTerm = Vector3.Dot(eye, eye) - 1f,
                Sx = scene.SunDirection.X, Sy = scene.SunDirection.Y, Sz = scene.SunDirection.Z,
                Grid = scene.Grid,
                GridOpacity = Math.Clamp(scene.GridOpacity, 0f, 1f),
                Regions = scene.Regions,
                FallbackR = scene.FallbackOcean.R,
                FallbackG = scene.FallbackOcean.G,
                FallbackB = scene.FallbackOcean.B
            };

            if (scene.Textures != null)
            {
                float degreesPerPixel = camera.SurfaceDegreesPerPixel;
                frame.Day = scene.Textures.Day.LevelFor(degreesPerPixel, MaxTexelsPerPixel);
                frame.Night = scene.Textures.Night?.LevelFor(degreesPerPixel, MaxTexelsPerPixel);
            }

            float cx = camera.CentreX;
            float cy = camera.CentreY;
            float focal = camera.Focal;
            int[] output = buffer;
            int[] sky = stars;
            int w = width;

            Parallel.For(0, height, y =>
            {
                int row = y * w;
                float b = (cy - (y + 0.5f)) / focal;
                float bTerm = b * b + 1f;
                float bRay = b * frame.EyeDotUp + frame.EyeDotForward;

                RowSpan(in frame, bTerm, bRay, cx, focal, w, out int xStart, out int xEnd);
                Array.Copy(sky, row, output, row, xStart);
                Array.Copy(sky, row + xEnd, output, row + xEnd, w - xEnd);

                for (int x = xStart; x < xEnd; x++)
                {
                    float a = (x + 0.5f - cx) / focal;
                    int star = sky[row + x];
                    float r, g, bl;

                    // Ray quadratic: |eye + t·d|² = 1 with d = a·right + b·up + forward.
                    float quadA = a * a + bTerm;
                    float quadB = 2f * (a * frame.EyeDotRight + bRay);
                    float discriminant = quadB * quadB - 4f * quadA * frame.EyeDistanceTerm;

                    // How much the discriminant changes across one pixel; small |discriminant|
                    // against that means the silhouette runs through this pixel, so supersample it.
                    float slopeA = 4f * (quadB * frame.EyeDotRight - 2f * frame.EyeDistanceTerm * a);
                    float slopeB = 4f * (quadB * frame.EyeDotUp - 2f * frame.EyeDistanceTerm * b);
                    float edge = (MathF.Abs(slopeA) + MathF.Abs(slopeB)) / focal;

                    if (MathF.Abs(discriminant) < edge)
                    {
                        float o = 0.25f / focal;
                        ShadeAt(in frame, a - o, b - o, star, out float r0, out float g0, out float b0);
                        ShadeAt(in frame, a + o, b - o, star, out float r1, out float g1, out float b1);
                        ShadeAt(in frame, a - o, b + o, star, out float r2, out float g2, out float b2);
                        ShadeAt(in frame, a + o, b + o, star, out float r3, out float g3, out float b3);
                        r = (r0 + r1 + r2 + r3) * 0.25f;
                        g = (g0 + g1 + g2 + g3) * 0.25f;
                        bl = (b0 + b1 + b2 + b3) * 0.25f;
                    }
                    else
                    {
                        Shade(in frame, a, b, quadA, quadB, discriminant, star, out r, out g, out bl);
                    }

                    output[row + x] = Pack(r, g, bl);
                }
            });

            CopyToBitmap();
            LastFrameMilliseconds = watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        ///  The columns of one row whose rays pass close enough to the planet to touch the halo.
        ///  Every other pixel is bare star field, exactly what <see cref="Sky"/> would return, so
        ///  the row loop copies those instead of shading them. Solves the same closest-approach
        ///  test as Sky, |eye|² − (eye·d)²/|d|² &lt; (1 + HaloExtent)², as a quadratic in a.
        /// </summary>
        private static void RowSpan(in Frame f, float bTerm, float bRay, float cx, float focal, int width, out int start, out int end)
        {
            start = 0;
            end = width;
            float k = f.EyeDistanceTerm + 1f - (1f + HaloExtent) * (1f + HaloExtent);
            float quadA = k - f.EyeDotRight * f.EyeDotRight;
            if (k <= 0f || quadA <= 0f)
            {
                // Eye inside the halo, or a view where the halo isn't a closed shape on this
                // row: shade everything.
                return;
            }

            float quadB = -2f * f.EyeDotRight * bRay;
            float quadC = k * bTerm - bRay * bRay;
            float discriminant = quadB * quadB - 4f * quadA * quadC;
            if (discriminant <= 0f)
            {
                start = end = 0;
                return;
            }

            // Two pixels of slack either side covers rounding and the silhouette supersampling.
            float root = MathF.Sqrt(discriminant);
            float left = cx + focal * (-quadB - root) / (2f * quadA) - 0.5f;
            float right = cx + focal * (-quadB + root) / (2f * quadA) - 0.5f;
            start = (int)Math.Clamp(MathF.Floor(left) - 2f, 0f, width);
            end = (int)Math.Clamp(MathF.Ceiling(right) + 3f, start, width);
        }

        private static void ShadeAt(in Frame f, float a, float b, int star, out float r, out float g, out float bl)
        {
            float quadA = a * a + b * b + 1f;
            float quadB = 2f * (a * f.EyeDotRight + b * f.EyeDotUp + f.EyeDotForward);
            float discriminant = quadB * quadB - 4f * quadA * f.EyeDistanceTerm;
            Shade(in f, a, b, quadA, quadB, discriminant, star, out r, out g, out bl);
        }

        /// <summary>
        ///  Shades one ray. (a, b) is the ray direction on the image plane at unit focal length;
        ///  the ray is a·right + b·up + forward from the eye.
        /// </summary>
        private static void Shade(in Frame f, float a, float b, float quadA, float quadB, float discriminant, int star,
            out float r, out float g, out float bl)
        {
            float dx = a * f.Rx + b * f.Ux + f.Fx;
            float dy = a * f.Ry + b * f.Uy + f.Fy;
            float dz = a * f.Rz + b * f.Uz + f.Fz;

            if (discriminant <= 0f)
            {
                Sky(in f, dx, dy, dz, quadA, quadB, star, out r, out g, out bl);
                return;
            }

            float t = (-quadB - MathF.Sqrt(discriminant)) / (2f * quadA);
            float nx = f.Ex + t * dx, ny = f.Ey + t * dy, nz = f.Ez + t * dz;   // unit surface normal
            float invLength = 1f / MathF.Sqrt(quadA);
            float cosView = -(nx * dx + ny * dy + nz * dz) * invLength;          // normal · direction to eye

            if (ny > 1f) ny = 1f; else if (ny < -1f) ny = -1f;
            float lat = MathF.Asin(ny);
            float lon = MathF.Atan2(-nz, nx);
            float u = lon * (0.5f / MathF.PI) + 0.5f;
            float v = 0.5f - lat * (1f / MathF.PI);

            float dr, dg, db, water;
            if (f.Day != null)
            {
                GlobeTexture.Sample(f.Day, u, v, out dr, out dg, out db, out water);
            }
            else
            {
                dr = f.FallbackR; dg = f.FallbackG; db = f.FallbackB; water = 1f;
            }

            // Daylight: mostly even, dimmer and warmer towards the terminator.
            float sun = nx * f.Sx + ny * f.Sy + nz * f.Sz;
            float dayFactor = SmoothStep(TerminatorStart, TerminatorEnd, sun);
            float lit = sun > 0f ? sun : 0f;
            float shade = 0.36f + 0.64f * MathF.Sqrt(lit);
            float warm = SmoothStep(0f, 0.35f, sun);
            r = dr * shade;
            g = dg * shade * (0.84f + 0.16f * warm);
            bl = db * shade * (0.66f + 0.34f * warm);

            // Twilight: a narrow amber band along the terminator, where sunlight crosses the
            // most air before it lands.
            float dusk = sun - TwilightCentre;
            float twilight = MathF.Exp(-dusk * dusk * (1f / (TwilightWidth * TwilightWidth)));
            r *= 1f + 0.28f * twilight;
            g *= 1f + 0.04f * twilight;
            bl *= 1f - 0.22f * twilight;

            // Sun glint on water: a small, sharp highlight inside a faint sheen. Rolled off and
            // warm so it reads as reflected sunlight rather than a white hole in the ocean.
            if (water > 0.01f && lit > 0f)
            {
                float hx = f.Sx - dx * invLength, hy = f.Sy - dy * invLength, hz = f.Sz - dz * invLength;
                float ndoth = (nx * hx + ny * hy + nz * hz) / MathF.Sqrt(hx * hx + hy * hy + hz * hz);
                if (ndoth > 0.9f)
                {
                    float p2 = ndoth * ndoth, p4 = p2 * p2, p8 = p4 * p4, p16 = p8 * p8, p32 = p16 * p16;
                    float p64 = p32 * p32, p128 = p64 * p64, p256 = p128 * p128, p1024 = p256 * p256 * p256 * p256;
                    float glint = p1024 * 0.85f + p256 * 0.10f + p64 * 0.03f;
                    float s = glint * water * dayFactor * 255f;
                    s = 200f * s / (s + 200f);   // soft roll-off, so the centre never clips to a flat disc
                    r += s;
                    g += s * 0.92f;
                    bl += s * 0.78f;
                }
            }

            // Night: the Black Marble composite, which already carries faintly lit land.
            if (dayFactor < 0.999f)
            {
                float nr, ng, nb;
                if (f.Night != null)
                {
                    GlobeTexture.Sample(f.Night, u, v, out nr, out ng, out nb, out _);
                    nr *= 1.15f; ng *= 1.12f; nb *= 1.05f;
                }
                else
                {
                    nr = dr * 0.05f; ng = dg * 0.06f; nb = db * 0.09f;
                }
                r = nr + (r - nr) * dayFactor;
                g = ng + (g - ng) * dayFactor;
                bl = nb + (bl - nb) * dayFactor;
            }

            // Data overlays sit on the surface, under the atmosphere, dimmed a little at night.
            float latDeg = lat * (180f / MathF.PI);
            float lonDeg = lon * (180f / MathF.PI);
            if (f.Grid != null && f.GridOpacity > 0f)
            {
                int argb = f.Grid.Sample(latDeg, lonDeg);
                if (argb != 0)
                {
                    float alpha = ((argb >> 24) & 255) * (1f / 255f) * f.GridOpacity;
                    float k = 0.65f + 0.35f * dayFactor;
                    r += (((argb >> 16) & 255) * k - r) * alpha;
                    g += (((argb >> 8) & 255) * k - g) * alpha;
                    bl += ((argb & 255) * k - bl) * alpha;
                }
            }

            var regions = f.Regions;
            for (int i = 0; i < regions.Length; i++)
            {
                ref readonly var region = ref regions[i];
                if (latDeg < region.LatitudeMin || latDeg > region.LatitudeMax)
                {
                    continue;
                }
                float east = lonDeg - region.LongitudeMin;
                east -= 360f * MathF.Floor(east / 360f);
                if (east > region.LongitudeSpan)
                {
                    continue;
                }
                float k = 0.6f + 0.4f * dayFactor;
                r += (region.R * k - r) * region.Alpha;
                g += (region.G * k - g) * region.Alpha;
                bl += (region.B * k - bl) * region.Alpha;
            }

            // Limb darkening, then the blue haze that thickens towards the edge of the disc.
            float rim = 1f - cosView;
            if (rim < 0f) rim = 0f;
            rim *= rim;
            rim *= rim;
            float atmosphereLit = 0.10f + 0.90f * SmoothStep(-0.25f, 0.30f, sun);
            float haze = rim * atmosphereLit;
            float keep = (0.80f + 0.20f * cosView) * (1f - 0.9f * haze);
            r = r * keep + 255f * 0.52f * haze;
            g = g * keep + 255f * 0.72f * haze;
            bl = bl * keep + 255f * 1.00f * haze;
        }

        /// <summary>Rays that miss the planet: stars, plus the halo close to the silhouette.</summary>
        private static void Sky(in Frame f, float dx, float dy, float dz, float quadA, float quadB, int star,
            out float r, out float g, out float bl)
        {
            r = (star >> 16) & 255;
            g = (star >> 8) & 255;
            bl = star & 255;

            // Closest approach of the ray to the planet's centre, in radii; 1 is the silhouette.
            float closestSquared = (f.EyeDistanceTerm + 1f) - quadB * quadB / (4f * quadA);
            float x = MathF.Sqrt(closestSquared > 0f ? closestSquared : 0f) - 1f;
            if (x >= HaloExtent)
            {
                return;
            }

            // Direction of the limb point nearest this ray, to light the halo from the sun side.
            float tc = -quadB / (2f * quadA);
            float qx = f.Ex + tc * dx, qy = f.Ey + tc * dy, qz = f.Ez + tc * dz;
            float length = MathF.Sqrt(qx * qx + qy * qy + qz * qz);
            float sun = length > 0f ? (qx * f.Sx + qy * f.Sy + qz * f.Sz) / length : 0f;
            float atmosphereLit = 0.08f + 0.92f * SmoothStep(-0.30f, 0.25f, sun);

            float glow = (MathF.Exp(-x * 55f) * 0.62f + MathF.Exp(-x * 16f) * 0.14f) * atmosphereLit;
            float white = MathF.Exp(-x * 220f) * 0.20f * atmosphereLit;

            // The halo turns amber where the limb is at sunrise or sunset.
            float dusk = sun - TwilightCentre;
            float twilight = MathF.Exp(-dusk * dusk * (1f / (TwilightWidth * TwilightWidth * 4f))) * 0.85f;
            float haloR = 0.52f + (1.00f - 0.52f) * twilight;
            float haloG = 0.72f + (0.62f - 0.72f) * twilight;
            float haloB = 1.00f + (0.38f - 1.00f) * twilight;
            r += 255f * (haloR * glow + white);
            g += 255f * (haloG * glow + white);
            bl += 255f * (haloB * glow + white);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = (x - edge0) / (edge1 - edge0);
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Pack(float r, float g, float b)
        {
            int ri = (int)r, gi = (int)g, bi = (int)b;
            if (ri > 255) ri = 255; else if (ri < 0) ri = 0;
            if (gi > 255) gi = 255; else if (gi < 0) gi = 0;
            if (bi > 255) bi = 255; else if (bi < 0) bi = 0;
            return (255 << 24) | (ri << 16) | (gi << 8) | bi;
        }

        private void EnsureBuffers(int targetWidth, int targetHeight, Color background, bool showStars)
        {
            int bg = background.ToArgb();
            if (targetWidth == width && targetHeight == height && bg == starsBackground && showStars == starsVisible && bitmap != null)
            {
                return;
            }

            width = targetWidth;
            height = targetHeight;
            starsBackground = bg;
            starsVisible = showStars;
            buffer = new int[width * height];
            stars = MakeStars(width, height, bg, showStars);
            bitmap?.Dispose();
            bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        }

        /// <summary>
        ///  A fixed, seeded scatter of faint stars over the background so the field doesn't
        ///  twinkle between frames. Most are dim; a few get a soft four-pixel halo.
        /// </summary>
        private static int[] MakeStars(int width, int height, int background, bool show)
        {
            var field = new int[width * height];
            Array.Fill(field, background | unchecked((int)0xFF000000));
            if (!show)
            {
                return field;
            }

            var random = new Random(2026);
            int count = width * height / 1500;
            for (int i = 0; i < count; i++)
            {
                int x = random.Next(width);
                int y = random.Next(height);
                double q = random.NextDouble();
                float brightness = (float)(0.12 + 0.88 * q * q * q) * 175f;
                float tint = (float)random.NextDouble();
                float r = brightness * (0.78f + 0.22f * tint);
                float g = brightness * (0.84f + 0.12f * tint);
                float b = brightness * (0.86f + 0.14f * (1f - tint));

                AddLight(field, y * width + x, r, g, b);
                if (q > 0.94)
                {
                    if (x > 0) AddLight(field, y * width + x - 1, r * 0.35f, g * 0.35f, b * 0.35f);
                    if (x < width - 1) AddLight(field, y * width + x + 1, r * 0.35f, g * 0.35f, b * 0.35f);
                    if (y > 0) AddLight(field, (y - 1) * width + x, r * 0.35f, g * 0.35f, b * 0.35f);
                    if (y < height - 1) AddLight(field, (y + 1) * width + x, r * 0.35f, g * 0.35f, b * 0.35f);
                }
            }
            return field;
        }

        private static void AddLight(int[] field, int index, float r, float g, float b)
        {
            int pixel = field[index];
            field[index] = Pack(((pixel >> 16) & 255) + r, ((pixel >> 8) & 255) + g, (pixel & 255) + b);
        }

        private void CopyToBitmap()
        {
            if (bitmap == null)
            {
                return;
            }

            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                if (data.Stride == width * 4)
                {
                    Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(buffer, y * width, data.Scan0 + y * data.Stride, width);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public void Dispose()
        {
            bitmap?.Dispose();
            bitmap = null;
        }
    }
}
