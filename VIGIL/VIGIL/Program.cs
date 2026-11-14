using VIGIL.Forms;

namespace VIGIL
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customise application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            using (var loader = new LoaderForm())
            {
                if (loader.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }

            Application.Run(new MainForm());
        }
    }
}
