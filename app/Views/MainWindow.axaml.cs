using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BSAvatarConverter.ViewModels;

namespace BSAvatarConverter.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
    }

    private MainViewModel Vm => (MainViewModel)DataContext!;

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

    private async void PickGame_Click(object? sender, RoutedEventArgs e)
    {
        var dirs = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = L.Game });
        if (dirs.FirstOrDefault()?.TryGetLocalPath() is { } path) Vm.SetGame(path);
    }
}
