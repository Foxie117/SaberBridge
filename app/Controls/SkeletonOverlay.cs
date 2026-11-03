using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BSAvatarConverter.Controls;

/// <summary>Draws the bones of one preview frame; coordinates are frame pixels (same size as the control).</summary>
public sealed class SkeletonOverlay : Control
{
    public static readonly StyledProperty<float[][]?> SegmentsProperty = AvaloniaProperty.Register<SkeletonOverlay, float[][]?>(nameof(Segments));

    private static readonly Pen BodyPen = new(new SolidColorBrush(Color.Parse("#E62F8CFF")), 2.2, lineCap: PenLineCap.Round);
    private static readonly Pen PhysicsPen = new(new SolidColorBrush(Color.Parse("#E6FFB547")), 1.6, lineCap: PenLineCap.Round);
    private static readonly IBrush Joint = new SolidColorBrush(Color.Parse("#F2FFFFFF"));

    static SkeletonOverlay() => AffectsRender<SkeletonOverlay>(SegmentsProperty);

    public float[][]? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Segments == null) return;
        foreach (float[] s in Segments)
        {
            if (s.Length < 5) continue;
            Point a = new(s[0], s[1]), b = new(s[2], s[3]);
            context.DrawLine(s[4] >= 1 ? PhysicsPen : BodyPen, a, b);
        }

        foreach (float[] s in Segments)
        {
            if (s.Length < 5 || s[4] >= 1) continue;
            context.DrawEllipse(Joint, null, new Point(s[2], s[3]), 2.6, 2.6);
        }
    }
}
