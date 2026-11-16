using Dicta;
using Dicta.Services;
using Xunit;

namespace Dicta.Tests;

public class SettingsStoreTests
{
    private static string SettingsFile => Path.Combine(AppPaths.StateDirectory, "dicta-settings.json");

    [Fact]
    public void SaveKeepsTheKeyAndReloadsIt()
    {
        var store = new SettingsStore { ApiKey = "sk-TEST-0123456789", ModelName = "gpt-transcribe" };
        store.Save();

        var loaded = new SettingsStore();
        loaded.Load();

        Assert.Equal("sk-TEST-0123456789", loaded.ApiKey);
        Assert.Equal("gpt-transcribe", loaded.ModelName);
    }

    [Fact]
    public void SaveLocksTheFileDownEvenWhenItWasReadable()
    {
        if (OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory(AppPaths.StateDirectory);
        File.WriteAllText(SettingsFile, "{\"WelcomeDismissed\":true}");
        File.SetUnixFileMode(
            SettingsFile,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        new SettingsStore { ApiKey = "sk-TEST-0123456789" }.Save();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(SettingsFile));
    }

    [Fact]
    public void FreshSaveOnMacAndLinuxIsPrivate()
    {
        if (OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory(AppPaths.StateDirectory);
        if (File.Exists(SettingsFile))
            File.Delete(SettingsFile);

        new SettingsStore { ApiKey = "sk-TEST-0123456789" }.Save();

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(SettingsFile));
    }
}
