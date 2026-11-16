using System.Text.Json;

namespace Dicta.Services;

sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(AppPaths.StateDirectory, "dicta-settings.json");

    public bool WelcomeDismissed { get; set; }

    public string? ApiKey { get; set; }

    public string? ModelName { get; set; }

    public void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var loaded = JsonSerializer.Deserialize<SettingsStore>(File.ReadAllText(_path));
            if (loaded != null)
            {
                WelcomeDismissed = loaded.WelcomeDismissed;
                ApiKey = loaded.ApiKey;
                ModelName = loaded.ModelName;
            }
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
            if (File.Exists(_path))
                RestrictToOwner(_path);
            Write(JsonSerializer.Serialize(this, JsonOptions));
            RestrictToOwner(_path);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't save settings.", ex);
        }
    }

    private void Write(string json)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(_path, json);
            return;
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        using var stream = new FileStream(_path, options);
        using var writer = new StreamWriter(stream);
        writer.Write(json);
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't restrict the settings file.", ex);
        }
    }
}
