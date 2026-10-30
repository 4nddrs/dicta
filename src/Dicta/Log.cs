namespace Dicta;

static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null)
    {
        var detail = ex == null ? message : message + " " + ex;
        Write("ERROR", detail);
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.StateDirectory);
            var path = Path.Combine(AppPaths.StateDirectory, "dicta.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}";
            lock (Gate)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 200_000)
                    File.Delete(path);
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // Logging must never stop dictation.
        }
    }
}
