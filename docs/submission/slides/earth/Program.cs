using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using VIGIL.Controls.Globe;

/// <summary>
///  Draws the app's globe off screen, with nothing on it, and crops the top of the planet into a
///  16:9 picture for the cover slide: the limb's highest point at a chosen height, and the limb
///  meeting the two bottom corners.
/// </summary>
internal static class Program
{
    // The slide, in its own pixels.
    private const double SlideWidth = 1920, SlideHeight = 1080;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 6)
        {
            Console.WriteLine("usage: earth <out folder> <name> <globe width px> <centre lat> <centre lon> <sun time UTC, ISO> [limb peak y on the slide = 660]");
            return 2;
        }
        string folder = args[0], name = args[1];
        int width = int.Parse(args[2], CultureInfo.InvariantCulture);
        double latitude = double.Parse(args[3], CultureInfo.InvariantCulture);
        double longitude = double.Parse(args[4], CultureInfo.InvariantCulture);
        var sun = DateTime.Parse(args[5], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        double peak = args.Length > 6 ? double.Parse(args[6], CultureInfo.InvariantCulture) : 660;
        Directory.CreateDirectory(folder);

        Application.SetHighDpiMode(HighDpiMode.DpiUnawareGdiScaled);
        Application.EnableVisualStyles();

        // The planet fits the control's smaller side, so the width sets its size and the extra
        // height is the sky the crop needs above the limb.
        int height = width + (int)(width * 0.9);
        using var form = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-6000, -6000),
            ShowInTaskbar = false,
            ClientSize = new Size(400, 400),
        };
        using var globe = new GlobeControl
        {
            Location = Point.Empty,
            Size = new Size(width, height),
            AutoRotate = false,
            ShowHud = false,
            ShowAttribution = false,
            ShowStars = true,
            SunTimeUtc = sun,
        };
        form.Controls.Add(globe);
        form.Show();
        globe.CentreOn(latitude, longitude);

        var until = DateTime.UtcNow.AddSeconds(60);
        while (!globe.TexturesLoaded && DateTime.UtcNow < until)
        {
            Pump(0.1);
        }
        if (!globe.TexturesLoaded)
        {
            Console.WriteLine("the textures did not load");
            return 1;
        }

        // The globe draws a rough frame while anything moves and the full one once it is still.
        using var render = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            Pump(1.5);
            globe.DrawToBitmap(render, new Rectangle(Point.Empty, globe.Size));
            if (globe.LastRenderScale >= 1f)
            {
                break;
            }
        }
        Console.WriteLine($"render scale {globe.LastRenderScale}, frame {globe.LastFrameMilliseconds:0} ms, control {width} x {height}");

        // Where the planet's edge is, from the globe itself.
        int centreX = width / 2, centreY = height / 2;
        int top = Edge(y => globe.TryGetSurfacePoint(new Point(centreX, y), out _, out _), 0, centreY);
        int left = Edge(x => globe.TryGetSurfacePoint(new Point(x, centreY), out _, out _), 0, centreX);
        double radius = ((centreY - top) + (centreX - left)) / 2.0;
        Console.WriteLine($"planet radius {radius:0.0} px: {centreY - top} up, {centreX - left} across, {2 * radius / width:0.000} of the width");

        // A circle whose highest point is at `peak` and that passes through both bottom corners.
        double drop = SlideHeight - peak;
        double slideRadius = (SlideWidth / 2 * SlideWidth / 2 + drop * drop) / (2 * drop);
        double scale = radius / slideRadius;
        var crop = new RectangleF((float)(centreX - SlideWidth / 2 * scale), (float)(top - peak * scale), (float)(SlideWidth * scale), (float)(SlideHeight * scale));
        Console.WriteLine($"slide radius {slideRadius:0.0}, {scale:0.000} render px per slide px, crop {crop}");
        if (crop.Top < 0 || crop.Left < 0 || crop.Right > width)
        {
            Console.WriteLine("the crop leaves the render: make the globe wider");
            return 1;
        }

        int outWidth = (int)Math.Round(SlideWidth * Math.Min(scale, 1.5)), outHeight = (int)Math.Round(outWidth * SlideHeight / SlideWidth);
        using var cover = new Bitmap(outWidth, outHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(cover))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(render, new RectangleF(0, 0, outWidth, outHeight), crop, GraphicsUnit.Pixel);
        }
        string path = Path.Combine(folder, name + ".png");
        cover.Save(path, ImageFormat.Png);
        Console.WriteLine($"{path}: {outWidth} x {outHeight}, {new FileInfo(path).Length / 1024} KB");

        var corner = cover.GetPixel(4, 4);
        Console.WriteLine($"sky at the top-left corner: #{corner.R:X2}{corner.G:X2}{corner.B:X2}");
        return 0;
    }

    /// <summary>The first position from <paramref name="from"/> towards <paramref name="to"/> that is on the planet.</summary>
    private static int Edge(Func<int, bool> onPlanet, int from, int to)
    {
        int low = from, high = to;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (onPlanet(middle)) high = middle; else low = middle;
        }
        return high;
    }

    private static void Pump(double seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(8);
        }
    }
}
