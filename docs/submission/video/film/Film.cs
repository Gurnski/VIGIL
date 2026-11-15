using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using VIGIL.Controls;
using VIGIL.Controls.Chat;
using VIGIL.Controls.Evidence;
using VIGIL.Controls.Globe;
using VIGIL.Controls.Zones;
using VIGIL.Data;
using VIGIL.Forms;
using VIGIL.Models;
using VIGIL.Pages;
using VIGIL.Theming;

/// <summary>
///  Films the real app, off screen, through a short storyboard. Every frame is the app's own
///  window drawn to a bitmap, with a caption added underneath. Frames are saved with the time
///  they were taken, and motion is run at a fraction of its speed so the film is smooth when
///  the times are divided back.
/// </summary>
internal static class Film
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, MK_LBUTTON = 1;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private)!.GetValue(owner)!;

    // ----- The recorder -----

    private static string folder = "";
    // The film is 1920 by 1080: the app's window above, and a band for the caption under it.
    private const int FilmWidth = 1920, FilmHeight = 1080, Band = 108;
    private static int width = FilmWidth, height = FilmHeight;
    private static double slow = 2.5;
    private static readonly List<(string File, double Seconds)> frames = new();
    private static readonly Stopwatch clock = new();
    private static string caption = "";
    private static PointF? pointer;
    private static MainForm main = null!;
    private static Control[] floating = Array.Empty<Control>();
    private static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
    private static readonly EncoderParameters Quality = new(1) { Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L) } };

    private static string Save(Bitmap bitmap)
    {
        string file = Path.Combine(folder, "f" + frames.Count.ToString("00000", CultureInfo.InvariantCulture) + ".jpg");
        bitmap.Save(file, Jpeg, Quality);
        return file;
    }

    /// <summary>The caption band along the bottom, and the pen's position when there is one.</summary>
    private static void Dress(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        if (pointer is PointF at)
        {
            using var ring = new Pen(Theme.Accent, 2.5f);
            g.DrawEllipse(ring, at.X - 9, at.Y - 9, 18, 18);
            using var dot = new SolidBrush(Theme.Accent);
            g.FillEllipse(dot, at.X - 3, at.Y - 3, 6, 6);
        }
        int band = Band;
        using var back = new SolidBrush(Theme.Background);
        g.FillRectangle(back, 0, height - band, width, band);
        using var line = new Pen(Theme.Border, 1f);
        g.DrawLine(line, 0, height - band + 0.5f, width, height - band + 0.5f);
        if (caption.Length == 0)
        {
            return;
        }
        using var font = new Font("Segoe UI Variable Display", height / 38f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Theme.TextPrimary);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(caption, font, brush, new RectangleF(width * 0.06f, height - band, width * 0.88f, band), format);
    }

    /// <summary>Films the window for a stretch of real time, at whatever rate frames can be taken.</summary>
    private static void Record(double realSeconds, Action<double>? eachFrame = null)
    {
        clock.Start();
        double start = clock.Elapsed.TotalSeconds;
        while (true)
        {
            double elapsed = clock.Elapsed.TotalSeconds - start;
            if (elapsed >= realSeconds)
            {
                break;
            }
            eachFrame?.Invoke(elapsed / realSeconds);
            Application.DoEvents();
            double taken = clock.Elapsed.TotalSeconds;
            using var app = Capture.Window(main, floating);
            using var bitmap = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.DrawImageUnscaled(app, 0, 0);
                Dress(g);
            }
            frames.Add((Save(bitmap), taken));
            Application.DoEvents();
        }
        // A marker for where this stretch ended, so the last frame is held until then.
        frames.Add(("", clock.Elapsed.TotalSeconds));
        clock.Stop();
    }

    /// <summary>A still frame held for a time on the film, such as a title.</summary>
    private static void Card(double filmSeconds, Action<Graphics> draw)
    {
        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            draw(g);
        }
        clock.Start();
        double now = clock.Elapsed.TotalSeconds;
        frames.Add((Save(bitmap), now));
        // Cards are not slowed, so their real time is the film time multiplied back up.
        double until = now + filmSeconds * slow;
        while (clock.Elapsed.TotalSeconds < until)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        frames.Add(("", clock.Elapsed.TotalSeconds));
        clock.Stop();
    }

    private static void Text(Graphics g, string text, float size, Color colour, float centreY, FontStyle style = FontStyle.Regular, string family = "Segoe UI Variable Display")
    {
        using var font = new Font(family, size, style, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(colour);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, new RectangleF(width * 0.08f, centreY - size * 1.5f, width * 0.84f, size * 3f), format);
    }

    /// <summary>Lets the app run between shots. The film's clock only runs inside a shot, so this is not on the film.</summary>
    private static void Pump(double realSeconds) => Capture.Pump(realSeconds);

    /// <summary>
    ///  Puts the sun over a longitude, at today's date, so the part of the globe being filmed is in
    ///  daylight. The globe's own read-out then says "Sunlight at" that time and not "Live sunlight".
    /// </summary>
    private static void Daylight(GlobeControl globe, double longitude)
    {
        globe.SunTimeUtc = DateTime.UtcNow.Date.AddHours(12 - longitude / 15.0);
    }

    /// <summary>Saves the app's window as it stands, with no caption, for the presentation.</summary>
    private static void Still(string name)
    {
        Directory.CreateDirectory(Path.Combine(folder, "stills"));
        using var app = Capture.Window(main, floating);
        app.Save(Path.Combine(folder, "stills", name + ".png"), ImageFormat.Png);
    }

    private static void Mouse(Control control, int message, int x, int y, bool down) =>
        SendMessage(control.Handle, message, (IntPtr)(down ? MK_LBUTTON : 0), (IntPtr)((y << 16) | (x & 0xFFFF)));

    private static void Click(Control control, int x, int y)
    {
        Mouse(control, WM_MOUSEMOVE, x, y, false);
        Mouse(control, WM_LBUTTONDOWN, x, y, true);
        Mouse(control, WM_LBUTTONUP, x, y, false);
    }

    /// <summary>Writes the list of frames with how long each shows, for the encoder.</summary>
    private static void WriteList()
    {
        var lines = new List<string>();
        string last = "";
        for (int i = 0; i < frames.Count - 1; i++)
        {
            if (frames[i].File.Length == 0)
            {
                continue;
            }
            double duration = Math.Max(0.001, (frames[i + 1].Seconds - frames[i].Seconds) / slow);
            lines.Add("file '" + Path.GetFileName(frames[i].File) + "'");
            lines.Add("duration " + duration.ToString("0.0000", CultureInfo.InvariantCulture));
            last = frames[i].File;
        }
        if (last.Length > 0)
        {
            lines.Add("file '" + Path.GetFileName(last) + "'");       // the concat format wants the last file named again
        }
        File.WriteAllLines(Path.Combine(folder, "frames.txt"), lines);
        double total = frames.Count > 1 ? (frames[^1].Seconds - frames[0].Seconds) / slow : 0;
        Console.WriteLine($"film: {frames.Count(f => f.File.Length > 0)} frames, {total:0.0} seconds on film");
    }

    // ----- The storyboard -----

    public static int Run(string address, string output)
    {
        folder = output;
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, true);
        }
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("VIGIL_API_URL", address);

        var service = new ResultsClient(address);
        var results = Task.Run(() => service.SnapshotAsync()).GetAwaiter().GetResult();
        var counts = results.Run!.Counts;
        Console.WriteLine($"results: run {results.Run.Id}, {results.Findings.Count} findings, {results.Grids.Count} layers, screen {Screen.PrimaryScreen!.Bounds.Size}, scale {Theme.SystemScale}");

        main = new MainForm(results) { StartPosition = FormStartPosition.Manual, Location = new Point(-6000, -6000), ShowInTaskbar = false };
        main.ClientSize = new Size(FilmWidth, FilmHeight - Band);
        main.Show();
        Pump(0.5);
        Console.WriteLine($"window client area {main.ClientSize.Width}x{main.ClientSize.Height}, film {width}x{height}");

        var rail = Field<NavRail>(main, "navRail");
        var pages = Field<Dictionary<string, Control>>(main, "openedPages");
        var panel = Field<ChatPanel>(main, "chatPanel");
        var launcher = Field<VigilButton>(main, "chatLauncher");
        floating = new Control[] { launcher, Field<VigilButton>(main, "presentButton"), panel };

        rail.Select("map");
        Pump(0.5);
        var map = (MapPage)pages["map"];
        var globe = Field<GlobeControl>(map, "globe");
        for (int i = 0; i < 300 && !globe.TexturesLoaded; i++) Pump(0.1);
        var layers = Field<SegmentedControl>(map, "layers");
        var list = Field<FindingsList>(map, "findingsList");
        var sideTabs = Field<SegmentedControl>(map, "sideTabs");
        globe.AutoRotateSpeed = (float)(4.0 / slow);
        typeof(GlobeControl).GetField("lastInteractionSeconds", Private)!.SetValue(globe, double.NegativeInfinity);

        Storyboard.Play(results, service, rail, pages, map, globe, layers, list, sideTabs, panel);
        WriteList();
        main.Close();
        return 0;
    }

    /// <summary>The shots, in order. Kept apart from the recorder so it reads as a script.</summary>
    private static class Storyboard
    {
        public static void Play(ResultsDocument results, ResultsClient service, NavRail rail, Dictionary<string, Control> pages, MapPage map,
            GlobeControl globe, SegmentedControl layers, FindingsList list, SegmentedControl sideTabs, ChatPanel panel)
        {
            var counts = results.Run!.Counts;
            var retest = results.Run.Retest;
            string tests = counts.TestsRun.ToString("N0", British);
            string significant = counts.TestsSignificant.ToString("N0", British);

            // 1. Title.
            caption = "";
            Card(2.0, g =>
            {
                Text(g, "VIGIL", height / 6.5f, Theme.TextPrimary, height * 0.40f, FontStyle.Bold);
                Text(g, "An Earth system trend detective", height / 22f, Theme.TextSecondary, height * 0.55f);
                Text(g, "NASA Space Apps Challenge 2026", height / 38f, Theme.TextMuted, height * 0.66f);
            });

            // 2. The overview: what the scan covered.
            rail.Select("overview");
            Pump(0.8);
            caption = "Twenty-five years of NASA records, scanned cell by cell, with nothing chosen in advance.";
            Record(2.2 * slow);
            Still("overview");

            // 3. The globe, layer by layer.
            rail.Select("map");
            Daylight(globe, 70);
            globe.AutoRotate = true;
            typeof(GlobeControl).GetField("lastInteractionSeconds", Private)!.SetValue(globe, double.NegativeInfinity);
            globe.FlyTo(15, 20, 1.15, TimeSpan.FromSeconds(0.2));
            Pump(1.0);
            caption = tests + " tests. " + significant + " significant trends after correcting for how many were run.";
            int layerCount = layers.Items.Count;
            double perLayer = 0.9 * slow;
            for (int i = 0; i < Math.Min(layerCount, 3); i++)
            {
                layers.SelectedIndex = i;
                typeof(GlobeControl).GetField("lastInteractionSeconds", Private)!.SetValue(globe, double.NegativeInfinity);
                Record(perLayer);
                if (i == 0)
                {
                    Still("globe");
                }
            }

            // 4. The top-ranked finding.
            globe.AutoRotate = false;
            var top = results.Findings[0];
            caption = "The top of the ranking is whatever the scan put first.";
            list.SelectFinding(top);
            globe.FlyTo(top.Cell.CentreLatitude, top.Cell.CentreLongitude, 3.2, TimeSpan.FromSeconds(1.8 * slow));
            Record(2.3 * slow);
            Still("top-finding");

            // 5. Its evidence.
            typeof(MainForm).GetMethod("ShowEvidence", Private)!.Invoke(main, new object[] { top.Id });
            Pump(0.3);
            var evidence = (EvidencePage)pages["evidence"];
            var scroller = Field<StillPanel>(evidence, "scroller");
            var view = Field<EvidenceView>(evidence, "view");
            caption = "Every finding opens into its evidence: the series, the uncertainty, the method and its limits.";
            Record(1.5 * slow);
            Still("evidence");
            int travel = Math.Max(0, Math.Min(view.Height - scroller.ClientSize.Height, (int)(scroller.ClientSize.Height * 0.9)));
            Record(1.7 * slow, progress => scroller.AutoScrollPosition = new Point(0, (int)(travel * Ease(progress))));

            // 6. A trend the re-test contradicted.
            var contradicted = results.Findings.FirstOrDefault(f => f.Standing.Retest?.Outcome == Retest.Opposite);
            if (contradicted != null && retest != null)
            {
                evidence.ShowFinding(contradicted.Id);
                Pump(0.4);
                caption = "Where a second record exists, each trend is tested against it, and the finding says how that went.";
                Record(2.3 * slow);
                Still("retest");
            }

            // 7. The layer standing on the globe, then year by year. The service gives the years: without it this shot is the relief alone.
            rail.Select("map");
            Daylight(globe, -58);
            globe.ClearRegions();
            layers.SelectedIndex = Math.Max(0, results.Grids.FindIndex(grid => grid.Variable == "precipitation"));
            globe.FlyTo(-9, -58, 2.3, TimeSpan.FromSeconds(0.3));
            Pump(1.2);
            var reliefToggle = Field<ToggleSwitch>(map, "reliefToggle");
            var yearsToggle = Field<ToggleSwitch>(map, "yearsToggle");
            var yearBar = Field<YearBar>(map, "yearBar");
            reliefToggle.Checked = true;                 // leans the view over by itself
            Pump(2.0);
            caption = "Relief: each cell rises by the size of its trend.";
            Record(1.3 * slow);
            Still("relief");
            yearsToggle.Checked = true;
            for (int i = 0; i < 100 && !yearBar.Visible && yearsToggle.Checked; i++) Pump(0.1);
            if (yearBar.Visible)
            {
                double playing = 3.7;
                yearBar.SelectedIndex = 0;
                yearBar.StepMilliseconds = (int)(playing * slow * 1000 / 25);
                Pump(0.3);
                caption = "And year by year: watch a change arrive.";
                yearBar.Playing = true;
                Record(playing * slow);
                yearBar.Playing = false;
                yearBar.SelectedIndex = 22;
                Pump(0.3);
                Still("years");
            }
            yearsToggle.Checked = false;
            reliefToggle.Checked = false;
            globe.TiltTo(0);
            Pump(2.0);

            // 8. A zone, drawn by hand.
            sideTabs.SelectedIndex = 1;
            var zonePanel = Field<ZonePanel>(map, "zonePanel");
            var nameBar = Field<ZoneNameBar>(map, "zoneNameBar");
            var zones = Field<ZoneService>(map, "zones");
            Click(Field<VigilButton>(zonePanel, "draw"), 10, 10);
            caption = "Draw any region on the globe.";
            int cx = globe.Width / 2, cy = globe.Height / 2;
            var stroke = Enumerable.Range(0, 121).Select(i =>
            {
                double a = i * Math.PI * 2 / 120;
                double r = globe.Height * 0.25 + globe.Height * 0.06 * Math.Sin(3 * a);
                return (X: cx + (int)(r * Math.Cos(a)), Y: cy + (int)(r * 0.8 * Math.Sin(a)));
            }).ToList();
            var origin = main.PointToClient(globe.PointToScreen(Point.Empty));
            Record(0.3 * slow);
            Mouse(globe, WM_MOUSEMOVE, stroke[0].X, stroke[0].Y, false);
            Mouse(globe, WM_LBUTTONDOWN, stroke[0].X, stroke[0].Y, true);
            int sent = 0;
            Record(2.0 * slow, progress =>
            {
                int upTo = Math.Min(stroke.Count - 1, (int)(progress * stroke.Count));
                for (; sent <= upTo; sent++)
                {
                    Mouse(globe, WM_MOUSEMOVE, stroke[sent].X, stroke[sent].Y, true);
                }
                pointer = new PointF(origin.X + stroke[upTo].X, origin.Y + stroke[upTo].Y);
            });
            for (; sent < stroke.Count; sent++)
            {
                Mouse(globe, WM_MOUSEMOVE, stroke[sent].X, stroke[sent].Y, true);
            }
            Mouse(globe, WM_LBUTTONUP, stroke[^1].X, stroke[^1].Y, false);
            pointer = null;
            Field<TextBox>(nameBar, "textBox").Text = "Southern Amazon";
            Record(0.6 * slow);
            Click(Field<VigilButton>(nameBar, "save"), 10, 10);
            caption = "VIGIL works out its trends on the spot, with the same tests as the scan.";
            Record(2.4 * slow);
            Still("zone");

            // 9. The Detective. With a service that holds a key, a real question is asked and its real
            // answer filmed. Without one, the panel is shown as it opens: no answer is ever staged.
            typeof(MainForm).GetMethod("SetChatOpen", Private)!.Invoke(main, new object[] { true });
            caption = "Ask the Detective. It answers only from the findings, and shows what it looked up.";
            bool live = false;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string health = Task.Run(() => http.GetStringAsync(service.Address + "/health")).GetAwaiter().GetResult();
                live = health.Replace(" ", "").Contains("\"detective\":true");
            }
            catch (Exception)
            {
                live = false;
            }
            Record((live ? 1.2 : 2.0) * slow);
            if (live)
            {
                var chatView = Field<ChatView>(panel, "view");
                var chats = Field<ChatService>(main, "chats");
                string question = "Where is rainfall falling fastest, and how sure is that?";
                typeof(ChatView).GetMethod("Send", Private)!.Invoke(chatView, new object[] { question });
                Record(1.0 * slow);
                // The wait for the model is not filmed.
                for (int i = 0; i < 850 && chats.IsWaiting(chatView.Conversation); i++) Pump(0.1);
                var answer = chatView.Conversation.Messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
                Console.WriteLine($"film: the Detective answered: source {answer?.Source}, status {answer?.Status}, citations {answer?.Citations.Count}, steps {answer?.Steps.Count}");
                Record(4.5 * slow);
                Still("detective");
                foreach (var made in chats.Conversations.Where(c => c.Title.StartsWith("Where is rainfall falling fastest", StringComparison.Ordinal)).ToList())
                {
                    chats.Delete(made);
                }
            }
            typeof(MainForm).GetMethod("SetChatOpen", Private)!.Invoke(main, new object[] { false });

            // 10. The end card.
            caption = "";
            Card(2.4, g =>
            {
                Text(g, "VIGIL", height / 9f, Theme.TextPrimary, height * 0.34f, FontStyle.Bold);
                Text(g, "Discovery first. Evidence for every finding.", height / 24f, Theme.TextSecondary, height * 0.47f);
                Text(g, "Data: NASA MERRA-2, GPM IMERG, GLDAS, MODIS Terra and Aqua, GISTEMP, GPCP. Earthquakes: USGS.", height / 44f, Theme.TextMuted, height * 0.62f);
                Text(g, "Every frame of this film is the app itself, showing the real scan.", height / 44f, Theme.TextMuted, height * 0.68f);
            });

        }

        private static double Ease(double t) => t * t * (3 - 2 * t);
    }
}
