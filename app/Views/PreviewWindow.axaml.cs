using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SaberBridge.ViewModels;

namespace SaberBridge.Views;

public partial class PreviewWindow : Window
{
    private Point? _dragFrom;

    public PreviewWindow()
    {
        InitializeComponent();
        Stage.PointerPressed += (_, e) => _dragFrom = e.GetPosition(Stage);
        Stage.PointerReleased += (_, _) => _dragFrom = null;
        Stage.PointerMoved += (_, e) =>
        {
            if (_dragFrom is not { } from || DataContext is not PreviewViewModel vm) return;
            Point now = e.GetPosition(Stage);

            // Viewbox scales the frame; convert the drag back to frame pixels
            double scale = Stage.Bounds.Width > 0 ? vm.FrameWidth / Stage.Bounds.Width : 1;
            double dx = (now.X - from.X) * scale;
            if (Math.Abs(dx) < vm.FrameWidth / Math.Max(1, vm.FrameCount)) return;
            vm.Scrub(dx);
            _dragFrom = now;
        };
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PreviewViewModel vm) return;
        await vm.Main.DeleteCard(vm.Card);
        if (vm.Card.InstalledFile == null && vm.Card.OutputFile == null) Close();
    }
}
