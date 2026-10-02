namespace Jjogae.Windows;

// macOS SettingsCardPlan: put each card in the shortest column.
internal sealed class MasonryPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? Math.Max(1, availableSize.Width) : 360;
        var columns = WindowLayout.Columns(width); var cardWidth = (width - (columns - 1) * 20) / columns;
        var bottoms = new double[columns];
        foreach (UIElement child in InternalChildren)
        { child.Measure(new Size(cardWidth, double.PositiveInfinity)); var column = Array.IndexOf(bottoms, bottoms.Min()); bottoms[column] += child.DesiredSize.Height + 20; }
        return new Size(width, Math.Max(0, bottoms.Max() - 20));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = WindowLayout.Columns(finalSize.Width); var width = (finalSize.Width - (columns - 1) * 20) / columns; var bottoms = new double[columns];
        foreach (UIElement child in InternalChildren)
        { var column = Array.IndexOf(bottoms, bottoms.Min()); child.Arrange(new Rect(column * (width + 20), bottoms[column], width, child.DesiredSize.Height)); bottoms[column] += child.DesiredSize.Height + 20; }
        return finalSize;
    }
}
