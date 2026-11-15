namespace VIGIL.Data
{
    /// <summary>
    ///  Where the app keeps what belongs to the person using it: the saved chats and zones.
    ///  It is <c>%LOCALAPPDATA%\VIGIL</c>, outside the repo. Setting the environment variable
    ///  <c>VIGIL_DATA_DIR</c> to a folder puts them there instead, so a rehearsal, a test or a
    ///  recording can run without touching the real ones.
    /// </summary>
    public static class AppFolder
    {
        public static string Path
        {
            get
            {
                string? chosen = Environment.GetEnvironmentVariable("VIGIL_DATA_DIR");
                return string.IsNullOrWhiteSpace(chosen)
                    ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VIGIL")
                    : chosen.Trim();
            }
        }
    }
}
