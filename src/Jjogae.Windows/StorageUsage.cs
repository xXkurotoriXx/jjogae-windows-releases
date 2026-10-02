namespace Jjogae.Windows;

internal sealed record StorageUsage(long Records, long Images, long Browser, long Updates, bool Incomplete)
{
    internal long Total => Records + Images + Browser + Updates;
    internal static StorageUsage Read(string root)
    {
        long records = 0, images = 0, browser = 0, updates = 0;
        var incomplete = false;
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        try
        {
            if (Directory.Exists(root)) foreach (var path in Directory.EnumerateFiles(root, "*", options))
            {
                try
                {
                    var size = new FileInfo(path).Length;
                    var category = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)[0];
                    if (category.Equals("updates", StringComparison.OrdinalIgnoreCase)) updates += size;
                    else if (category is "WebView2" or "YouTubeWebView2") browser += size;
                    else if (category.Equals("thumbnails", StringComparison.OrdinalIgnoreCase) || category == "background.png") images += size;
                    else records += size;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { incomplete = true; }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { incomplete = true; }
        return new(records, images, browser, updates, incomplete);
    }
}
