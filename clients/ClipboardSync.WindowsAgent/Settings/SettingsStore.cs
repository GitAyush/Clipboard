using System.Text.Json;
using System.IO;
using System.Linq;

namespace ClipboardSync.WindowsAgent.Settings;

public sealed class SettingsStore
{
    private readonly string _profile;
    private readonly string _dir;
    private readonly string _path;

    public SettingsStore(string? profileName, string? baseDirectory = null)
    {
        _profile = string.IsNullOrWhiteSpace(profileName) ? "default" : Sanitize(profileName);
        _dir = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardSync")
            : baseDirectory;
        _path = Path.Combine(_dir, $"settings.{_profile}.json");
    }

    public string SettingsPath => _path;

    public AppSettings LoadOrCreateDefaults()
    {
        Directory.CreateDirectory(_dir);
        if (!File.Exists(_path))
        {
            var created = new AppSettings();
            Save(created);
            return created;
        }

        try
        {
            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings is null) throw new InvalidOperationException("Settings file is empty/invalid.");
            if (settings.DeviceId == Guid.Empty) settings.DeviceId = Guid.NewGuid();
            if (string.IsNullOrWhiteSpace(settings.ServerBaseUrl)) settings.ServerBaseUrl = "http://localhost:5104";
            if (string.IsNullOrWhiteSpace(settings.DeviceName)) settings.DeviceName = Environment.MachineName;
            return settings;
        }
        catch
        {
            // Corrupt settings file: keep a backup and regenerate.
            var backup = _path + ".bak";
            try { File.Copy(_path, backup, overwrite: true); } catch { /* ignore */ }
            var created = new AppSettings();
            Save(created);
            return created;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(_dir);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_path, json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(s.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "default" : cleaned;
    }
}


