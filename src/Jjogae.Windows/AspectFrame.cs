namespace Jjogae.Windows;

/// <summary>Video height follows allocated width, including resize and DPI changes.</summary>
public sealed class AspectFrame : Decorator
{
    public const double VideoRatio = 16d / 9d;
    public AspectFrame() { ClipToBounds = true; HorizontalAlignment = HorizontalAlignment.Stretch; }
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsFinite(available.Width) ? Math.Max(0, available.Width) : 480;
        var size = new Size(width, width / VideoRatio); Child?.Measure(size); return size;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Width / VideoRatio));
        Clip = new RectangleGeometry(new Rect(0, 0, finalSize.Width, finalSize.Width / VideoRatio), 12, 12); return finalSize;
    }
}
