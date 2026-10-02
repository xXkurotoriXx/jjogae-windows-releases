namespace Jjogae.Core;

// Shared thresholds from macOS WindowLayoutPolicy and OverviewMetricLayout.
public static class WindowLayout
{
    public const double MinimumWidth = 430, MinimumHeight = 540, CompactBreakpoint = 720, MaximumPageWidth = 1560;
    public static bool Compact(double width) => double.IsFinite(width) && width > 0 && width < CompactBreakpoint;
    public static int Columns(double width) => double.IsFinite(width) && width >= 1120 ? 3 : double.IsFinite(width) && width >= 740 ? 2 : 1;
    public static int[] BalancedRows(int count, int columns)
    {
        if (count <= 0) return [];
        var rows = (count + Math.Max(1, columns) - 1) / Math.Max(1, columns);
        return Enumerable.Range(0, rows).Select(i => count / rows + (i < count % rows ? 1 : 0)).ToArray();
    }
}
