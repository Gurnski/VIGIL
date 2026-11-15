using VIGIL.Data;
using VIGIL.Models;
using VIGIL.Theming;

namespace VIGIL.Forms
{
    /// <summary>
    ///  Splash screen shown at startup. Runs app initialisation while cycling taglines,
    ///  then closes with DialogResult.OK so Program can open the main window.
    /// </summary>
    public partial class LoaderForm : Form
    {
        private const string LogoResource = "VIGIL.Assets.UoPHackathonLogo.jpg";
        private static readonly TimeSpan MinimumDisplayTime = TimeSpan.FromSeconds(6);
        private const float FadeStep = 0.1f;

        private static readonly string[] Taglines =
        {
            "Keeping watch on a changing planet.",
            "See what's changing, where, and whether it matters.",
            "Earth's long-term change, under watch."
        };

        private readonly Color taglineColour;
        private int taglineIndex;
        private float fadeProgress = 1f;
        private bool fadingOut;

        /// <summary>The results loaded during startup, for Program to hand to the main window. Null until the loader closes with OK.</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ResultsDocument? Results { get; private set; }

        public LoaderForm()
        {
            InitializeComponent();

            taglineColour = taglineLabel.ForeColor;
            taglineLabel.Text = Taglines[0];

            using var stream = typeof(LoaderForm).Assembly.GetManifestResourceStream(LogoResource);
            if (stream != null)
            {
                using var logo = new Bitmap(stream);
                logoPictureBox.Image = WhitenBackground(logo);
            }
        }

        /// <summary>
        ///  The logo JPG has an off-white, slightly noisy background that shows as a box on a
        ///  white form. Scale brightness so that background clips to pure white; black stays black.
        /// </summary>
        private static Bitmap WhitenBackground(Bitmap source)
        {
            Color corner = source.GetPixel(0, 0);
            int darkest = Math.Min(corner.R, Math.Min(corner.G, corner.B));
            float scale = 255f / Math.Max(darkest, 1) * 1.04f;

            var matrix = new System.Drawing.Imaging.ColorMatrix(new[]
            {
                new[] { scale, 0f, 0f, 0f, 0f },
                new[] { 0f, scale, 0f, 0f, 0f },
                new[] { 0f, 0f, scale, 0f, 0f },
                new[] { 0f, 0f, 0f, 1f, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f }
            });

            var result = new Bitmap(source.Width, source.Height);
            using var g = Graphics.FromImage(result);
            using var attributes = new System.Drawing.Imaging.ImageAttributes();
            attributes.SetColorMatrix(matrix);
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            return result;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var border = new Pen(Color.Gainsboro);
            e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            taglineTimer.Start();

            try
            {
                var minimumDisplay = Task.Delay(MinimumDisplayTime);
                Results = await InitialiseAppAsync();
                await minimumDisplay;
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex)
            {
                taglineTimer.Stop();
                MessageBox.Show(this, $"VIGIL failed to start:\n\n{ex.Message}", "VIGIL",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.Abort;
            }
        }

        /// <summary>
        ///  Startup work goes here (config, data catalogue, API checks, caches).
        ///  Runs off the UI thread so the spinner keeps turning. It loads the results: the scan
        ///  the analysis service holds when the service is running, then a stored scan beside the
        ///  exe (<c>snapshot.json</c>, which the download carries), and otherwise the snapshot
        ///  built into the app, which is made up and says so. A failure to read even that
        ///  stops the app with the reason shown.
        /// </summary>
        private static Task<ResultsDocument> InitialiseAppAsync()
        {
            return Task.Run(async () =>
            {
                try
                {
                    return await new ResultsClient().SnapshotAsync();
                }
                catch (ServiceUnavailableException)
                {
                    return ResultsReader.ReadBeside() ?? ResultsReader.ReadSnapshot();
                }
            });
        }

        private void TaglineTimer_Tick(object? sender, EventArgs e)
        {
            fadingOut = true;
            fadeTimer.Start();
        }

        private void FadeTimer_Tick(object? sender, EventArgs e)
        {
            if (fadingOut)
            {
                fadeProgress -= FadeStep;
                if (fadeProgress <= 0f)
                {
                    fadeProgress = 0f;
                    fadingOut = false;
                    taglineIndex = (taglineIndex + 1) % Taglines.Length;
                    taglineLabel.Text = Taglines[taglineIndex];
                }
            }
            else
            {
                fadeProgress += FadeStep;
                if (fadeProgress >= 1f)
                {
                    fadeProgress = 1f;
                    fadeTimer.Stop();
                }
            }

            taglineLabel.ForeColor = Draw.Blend(BackColor, taglineColour, fadeProgress);
        }
    }
}
