using System.Runtime.InteropServices;

namespace VIGIL.Theming
{
    /// <summary>
    ///  Windows-level dark mode: title bar colour and dark scrollbars.
    ///  Calls fail quietly on Windows versions that don't support them.
    /// </summary>
    internal static class WindowChrome
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

        public static void UseDarkTitleBar(Form form, Color caption)
        {
            int enabled = 1;
            DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int));

            // COLORREF is 0x00BBGGRR. Only Windows 11 honours a custom caption colour.
            int colour = caption.R | (caption.G << 8) | (caption.B << 16);
            DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref colour, sizeof(int));
        }

        public static void UseDarkScrollbars(Control control)
        {
            SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        }
    }
}
