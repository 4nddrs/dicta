namespace Dicta;

static class AppPaths
{
    public static string StateDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return AppContext.BaseDirectory;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "dicta");
        }
    }
}
