using Avalonia;
using SaberBridge.Core;

namespace SaberBridge;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppPaths.MigrateOldData();
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
