using System.Text.Json;

namespace Dicta.Services;

sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(AppPaths.StateDirectory, "dicta-settings.json");

    public bool WelcomeDismissed { get; set; }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var loaded = JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(_path));
            if (loaded != null)
                WelcomeDismissed = loaded.WelcomeDismissed;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't read settings.", ex);
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.StateDirectory);
            File.WriteAllText(_path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't save settings.", ex);
        }
    }
}
