// Films the VIGIL desktop app, off screen, for the demo video.
// usage: film <folder for the frames> <address of the analysis service>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: film <folder for the frames> <address of the analysis service>");
            return 2;
        }

        // The app's own files (saved chats and zones) go in a scratch folder, never the user's.
        string own = Path.Combine(Path.GetTempPath(), "vigil-film-" + Environment.ProcessId);
        Directory.CreateDirectory(own);
        Environment.SetEnvironmentVariable("VIGIL_DATA_DIR", own);

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // Application.Run keeps the UI thread's synchronisation context in place, so an await comes back to
        // the UI thread. This program pumps messages itself, so the context is pinned here instead.
        WindowsFormsSynchronizationContext.AutoInstall = false;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        return Film.Run(args[1], args[0]);
    }
}
