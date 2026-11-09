using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;

namespace SaberBridge.Views;

/// <summary>
/// Keeps a window inside the screen it opens on: on small or high-DPI screens (a 1366×768 laptop, 1920×1080 at
/// 150%) the designed size is larger than the work area, and the title bar or the bottom buttons ended up off-screen.
/// </summary>
public static class WindowFit
{
    public static void Attach(Window w)
    {
        w.Opened += (_, _) => Fit(w);
    }

    private static void Fit(Window w)
    {
        Screen? screen = w.Screens.ScreenFromWindow(w) ?? w.Screens.Primary;
        if (screen == null) return;

        PixelRect area = screen.WorkingArea;
        double scale = screen.Scaling;
        double maxW = area.Width / scale;
        double maxH = area.Height / scale;
        if (w.Width <= maxW && w.Height <= maxH) return;

        // leave a little room around it; the content scrolls where it has to
        w.MinWidth = Math.Min(w.MinWidth, maxW);
        w.MinHeight = Math.Min(w.MinHeight, maxH);
        w.Width = Math.Min(w.Width, maxW * 0.97);
        w.Height = Math.Min(w.Height, maxH * 0.97);
        int pw = (int)(w.Width * scale), ph = (int)(w.Height * scale);
        w.Position = new PixelPoint(area.X + (area.Width - pw) / 2, area.Y + (area.Height - ph) / 2);

        // Windows first squeezed the designed size to the screen and the content was laid out at that width; after the
        // resize above it kept that layout (cut off on the right) until the user touched the window: lay it out again
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (w.Content is Layoutable content)
            {
                content.InvalidateMeasure();
                content.InvalidateArrange();
            }

            w.InvalidateMeasure();
            w.InvalidateArrange();
            w.InvalidateVisual();
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
