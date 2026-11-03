using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BSAvatarConverter.ViewModels;
using BSAvatarConverter.Views;

namespace BSAvatarConverter;

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
            List<string> files = args.Where(a => !a.StartsWith("--")).ToList();
            if (files.Count > 0)
            {
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    await vm.AddPathsAsync(files);
                    if (args.Contains("--convert") && vm.ConvertCommand.CanExecute(null)) await vm.ConvertCommand.ExecuteAsync(null);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
