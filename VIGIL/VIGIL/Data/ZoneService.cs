using System.Text.Json;
using VIGIL.Models;

namespace VIGIL.Data
{
    /// <summary>
    ///  The zones the user has drawn and saved. They are kept in
    ///  <c>%LOCALAPPDATA%\VIGIL\zones.json</c>, outside the repo, beside the saved chats.
    ///  Call it from the UI thread: its event is raised on the thread that called it.
    /// </summary>
    public sealed class ZoneService
    {
        private readonly string path;
        private readonly List<Zone> zones = new();

        public ZoneService(string? storePath = null)
        {
            path = storePath ?? Path.Combine(AppFolder.Path, "zones.json");
            Load();
        }

        /// <summary>Raised when a zone is added or deleted.</summary>
        public event EventHandler? ListChanged;

        /// <summary>Saved zones, newest first.</summary>
        public IReadOnlyList<Zone> Zones => zones.OrderByDescending(zone => zone.Created).ToList();

        /// <summary>A name no saved zone has yet: "Zone 1", "Zone 2" and so on.</summary>
        public string NextName()
        {
            for (int number = zones.Count + 1; ; number++)
            {
                string name = "Zone " + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (zones.All(zone => !string.Equals(zone.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return name;
                }
            }
        }

        public void Add(Zone zone)
        {
            zones.Add(zone);
            Save();
            ListChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Delete(Zone zone)
        {
            if (zones.RemoveAll(saved => saved.Id == zone.Id) > 0)
            {
                Save();
                ListChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(path))
                {
                    var saved = JsonSerializer.Deserialize<List<Zone>>(File.ReadAllText(path), DetectiveClient.Json);
                    if (saved != null)
                    {
                        zones.AddRange(saved.Where(zone => zone.Points.Count >= 3));
                    }
                }
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            {
                // A damaged or unreadable file should not stop the app. Start with no saved zones.
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(zones, DetectiveClient.Json));
                File.Move(temporary, path, true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The zones still work for this session if they cannot be saved.
            }
        }
    }
}
