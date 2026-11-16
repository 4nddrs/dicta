using System.Runtime.CompilerServices;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Dicta.Tests;

internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Keeps every test away from the real settings file and log.
        var directory = Path.Combine(Path.GetTempPath(), "dicta-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("DICTA_STATE_DIR", directory);
    }
}
