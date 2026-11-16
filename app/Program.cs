using Avalonia;
using SaberBridge.Core;

namespace SaberBridge;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // started elevated by the app itself to switch Windows' crash dumps for Unity's processes
        if (args.Length == 3 && args[0] == "--dumps" && OperatingSystem.IsWindows()) return Diagnostics.SetDumpsElevated(args[1] == "on", args[2]);
        AppPaths.MigrateOldData();
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args);
        return 0;
    }
}
