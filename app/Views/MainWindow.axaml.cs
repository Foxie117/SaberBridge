using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SaberBridge.ViewModels;

namespace SaberBridge.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowFit.Attach(this);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DropZone.AddHandler(DragDrop.DragLeaveEvent, (_, _) => Highlight(false));
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);

        // the whole window accepts drops too, so a miss by a few pixels still works
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        LogBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty) LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
        };

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            vm.Confirm = ConfirmAsync;
            vm.PickFolder = PickFolderAsync;
            vm.ScrollLibraryToTop = () => LibraryScroll.Offset = default;
            vm.OpenPreviewWindow = card => new PreviewWindow { DataContext = new PreviewViewModel(vm, card) }.Show(this);
            vm.Setup.OpenGuideWindow = () =>
            {
                if (_setupWindow is { IsVisible: true })
                {
                    _setupWindow.Activate();
                    return;
                }

                _setupWindow = new SetupWindow { DataContext = vm.Setup };
                _setupWindow.Show(this);
            };
            vm.Setup.CloseGuideWindow = () => _setupWindow?.Close();
        };
    }

    private MainViewModel Vm => (MainViewModel)DataContext!;
    private SetupWindow? _setupWindow;

    private void Highlight(bool on)
    {
        DropDash.Stroke = on ? new SolidColorBrush(Color.Parse("#2F8CFF")) : new SolidColorBrush(Color.Parse("#3A3F58"));
        DropGlow.Background = on ? new SolidColorBrush(Color.Parse("#1A2340")) : (IBrush)this.FindResource("Panel")!;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool ok = e.Data.Contains(DataFormats.Files) && !Vm.IsBusy;
        e.DragEffects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        Highlight(ok);
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        Highlight(false);
        if (Vm.IsBusy) return;
        List<string> paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList() ?? new();
        if (paths.Count > 0) await Vm.AddPathsAsync(paths);
    }

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = L.Browse,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Models") { Patterns = new[] { "*.vrm", "*.pmx", "*.unitypackage", "*.zip" } },
                FilePickerFileTypes.All,
            },
        });
        List<string> paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) await Vm.AddPathsAsync(paths);
    }

    private async void PickUnity_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Unity" });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) Vm.SetUnity(path);
    }

    private async void BrowseSabers_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = L.Browse,
            FileTypeFilter = new[] { new FilePickerFileType("Sabers, notes") { Patterns = new[] { "*.saber", "*.bloq", "*.zip" } }, FilePickerFileTypes.All },
        });
        List<string> paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0) await Vm.AddPathsAsync(paths);
    }

    private async void PickQuestFolder_Click(object? sender, RoutedEventArgs e)
    {
        var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.QuestFolder });
        if (dirs.FirstOrDefault()?.TryGetLocalPath() is { } path) Vm.Sabers.SetOutputDir(path);
    }

    private async void PickGame_Click(object? sender, RoutedEventArgs e) => await Vm.PickGameAsync();

    private async Task<string?> PickFolderAsync()
    {
        var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.Game });
        return dirs.FirstOrDefault()?.TryGetLocalPath();
    }

    // a small modal over whichever of our windows is in front (the main one or a preview)
    private async Task<bool> ConfirmAsync(string title, string text)
    {
        Window owner = OwnedWindows.FirstOrDefault(w => w.IsActive) ?? this;
        bool result = false;
        Window dialog = new()
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = (IBrush)this.FindResource("Panel")!,
            Icon = Icon,
        };

        Button yes = new() { Content = L.Delete, Classes = { "ghost", "danger" } };
        Button no = new() { Content = L.Cancel, Classes = { "ghost" } };
        yes.Click += (_, _) => { result = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.Bold },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, FontSize = 13 },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
