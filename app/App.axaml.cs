using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SaberBridge.ViewModels;
using SaberBridge.Views;

namespace SaberBridge;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainViewModel vm = new();
            desktop.MainWindow = new MainWindow { DataContext = vm };

            // files dropped onto the exe (or passed on the command line) go straight into the queue;
            // "--convert" also starts the conversion
            string[] args = desktop.Args ?? Array.Empty<string>();
            // an .avatar opens its preview instead
            List<string> files = args.Where(a => !a.StartsWith("--")).ToList();
            List<string> avatars = files.Where(f => f.EndsWith(".avatar", StringComparison.OrdinalIgnoreCase)).ToList();
            files = files.Except(avatars).ToList();
            if (files.Count > 0 || avatars.Count > 0)
            {
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    foreach (string a in avatars) await vm.OpenAvatarFileAsync(a);
                    if (files.Count == 0) return;
                    await vm.AddPathsAsync(files, openTheirTab: true);
                    if (args.Contains("--convert") && vm.ConvertCommand.CanExecute(null)) await vm.ConvertCommand.ExecuteAsync(null);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
