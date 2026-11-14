using System.Drawing.Text;

namespace VIGIL.Theming
{
    /// <summary>
    ///  Colours, fonts, spacing and corner sizes for VIGIL's look: flat graphite surfaces, an
    ///  off-white accent, and colour kept for data and for meaning. Nothing is see-through, glows
    ///  or fades. Every control reads from here, so restyling the app means changing this file.
    ///
    ///  Surfaces are told apart by tone, not by outlines: a card is Surface on Background, an
    ///  input is SurfaceRaised on Surface. Border and Divider are for the few places that still
    ///  need a line. Sizes are in pixels at 100% scale; controls pass them through Px().
    /// </summary>
    public static class Theme
    {
        // Surfaces, darkest to lightest. Background is repeated in MainForm.Designer.cs, so keep it in step.
        public static readonly Color Background = FromHex(0x131415);
        public static readonly Color SurfaceLow = FromHex(0x17181A);
        public static readonly Color Surface = FromHex(0x1B1C1F);
        public static readonly Color SurfaceRaised = FromHex(0x232529);
        public static readonly Color SurfaceHover = FromHex(0x2C2E33);
        public static readonly Color Border = FromHex(0x383B41);
        public static readonly Color Divider = FromHex(0x26282C);
        public static readonly Color GridLine = FromHex(0x25272B);

        public static readonly Color TextPrimary = FromHex(0xEDECE8);
        public static readonly Color TextSecondary = FromHex(0xA6A5A0);
        public static readonly Color TextMuted = FromHex(0x7C7B77);
        public static readonly Color TextDisabled = FromHex(0x53524F);

        // The accent marks what you can act on and what is selected: the primary button, a
        // switch that is on, the selected nav item. It is off-white, so it never competes with data.
        public static readonly Color Accent = FromHex(0xE9E6DF);
        public static readonly Color AccentHover = FromHex(0xF7F5F0);
        public static readonly Color AccentPressed = FromHex(0xCDCAC3);
        public static readonly Color OnAccent = FromHex(0x151617);

        /// <summary>The colour of a measured series in a chart. Kept apart from Accent so either can change alone.</summary>
        public static readonly Color Series = FromHex(0x74A9DB);

        // Status colours. Rising or falling isn't good or bad on its own, so pick by meaning.
        // Use them for the one thing that carries the meaning (a value, a badge), not for decoration.
        public static readonly Color Positive = FromHex(0x5DBE8F);
        public static readonly Color Negative = FromHex(0xE4776E);
        public static readonly Color Warning = FromHex(0xDFA646);

        /// <summary>Alpha for a tinted fill behind coloured text, such as a badge.</summary>
        public const int TintAlpha = 30;

        public const int RadiusSmall = 3;
        public const int Radius = 4;

        // Spacing scale on a 4px base. SpaceN is N times 4.
        public const int Space1 = 4;
        public const int Space2 = 8;
        public const int Space3 = 12;
        public const int Space4 = 16;
        public const int Space5 = 20;
        public const int Space6 = 24;
        public const int Space8 = 32;
        public const int Space10 = 40;

        // Where the scale lands in the layout
        public const int Gutter = Space5;
        public const int CardPadding = Space5;
        public const int PagePadding = Space10;
        public const int PagePaddingTop = Space8;
        public const int ControlHeight = 36;
        public const int ControlHeightSmall = 28;
        public const int RailWidth = 240;
        public const int RailCollapsedWidth = 64;

        private static readonly string UiFamily = FirstInstalled("Segoe UI Variable Text", "Segoe UI");
        private static readonly string UiStrongFamily = FirstInstalled("Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Segoe UI");
        private static readonly string SmallFamily = FirstInstalled("Segoe UI Variable Small", "Segoe UI Variable Text", "Segoe UI");
        private static readonly string SmallStrongFamily = FirstInstalled("Segoe UI Variable Small Semibol", "Segoe UI Variable Text Semibold", "Segoe UI Semibold", "Segoe UI");
        private static readonly string DisplayFamily = FirstInstalled("Segoe UI Variable Display Semib", "Segoe UI Semibold", "Segoe UI");
        // Large type is set light: at 20pt and up a semibold shouts, and a lighter cut reads as more considered.
        private static readonly string HeadingFamily = FirstInstalled("Segoe UI Variable Display", "Segoe UI");
        private static readonly string FigureFamily = FirstInstalled("Segoe UI Variable Display Semil", "Segoe UI Semilight", "Segoe UI");
        // Classic Segoe UI has tabular figures; the Variable faces don't, so numbers that must line up use this.
        private static readonly string NumericFamily = FirstInstalled("Segoe UI");
        private static readonly string IconFamily = FirstInstalled("Segoe Fluent Icons", "Segoe MDL2 Assets");

        // Type scale: three text sizes (caption 8.5, body 10, title 14) and two display sizes.
        public static readonly Font Body = new(UiFamily, 10f);
        public static readonly Font BodyStrong = Strong(UiStrongFamily, 10f);
        public static readonly Font Caption = new(SmallFamily, 8.5f);
        public static readonly Font CaptionStrong = Strong(SmallStrongFamily, 8.5f);
        public static readonly Font Title = Strong(DisplayFamily, 14f);
        public static readonly Font Heading = new(HeadingFamily, 20f);
        public static readonly Font Display = new(FigureFamily, 26f);

        /// <summary>Body-sized figures that line up: hover read-outs, tables.</summary>
        public static readonly Font Numeric = new(NumericFamily, 10f);
        public static readonly Font NumericStrong = new(NumericFamily, 10f, FontStyle.Bold);
        /// <summary>Caption-sized figures that line up: axis labels.</summary>
        public static readonly Font NumericSmall = new(NumericFamily, 8.5f);

        private static readonly Dictionary<float, Font> iconFonts = new();

        /// <summary>
        ///  Icon font at the given point size. Use with glyphs from <see cref="Icons"/>.
        /// </summary>
        public static Font Icon(float size)
        {
            if (!iconFonts.TryGetValue(size, out var font))
            {
                font = new Font(IconFamily, size);
                iconFonts[size] = font;
            }
            return font;
        }

        private static Color FromHex(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        /// <summary>
        ///  A heavier weight. Uses the family's own semibold face when there is one, otherwise bold.
        /// </summary>
        private static Font Strong(string family, float size)
        {
            bool hasWeightFace = family.Contains("Semib", StringComparison.OrdinalIgnoreCase);
            return hasWeightFace ? new Font(family, size) : new Font(family, size, FontStyle.Bold);
        }

        private static string FirstInstalled(params string[] families)
        {
            using var installed = new InstalledFontCollection();
            var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return families.FirstOrDefault(names.Contains) ?? families[^1];
        }
    }
}
