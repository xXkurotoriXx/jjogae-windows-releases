namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    internal void PreparePreview()
    {
        if (!app.IsTest) throw new InvalidOperationException("Preview requires isolated storage.");
        SeedVisualChecks(); app.State.Settings.Theme = "light"; Title = "쪼개 상황실 · 검토용"; Render();
    }
    public async Task<string> VerifyMacParity(string? screenshots)
    {
        if (!app.IsTest) throw new InvalidOperationException("QA requires isolated storage.");
        var report = new List<string>();
        void Check(bool pass, string name) => report.Add((pass ? "PASS: " : "FAIL: ") + name);
        SeedVisualChecks();
        var emojiLabel = Text("카페 🧧 🫧 ❤️ 👍🏽 👨‍👩‍👧‍👦 🇰🇷 1️⃣", 20);
        Check(emojiLabel.EmojiInlines.Count() == 7, "emoji sequences render as seven complete color glyphs");
        Check(emojiLabel.EmojiInlines.All(e => e.Child.Source is not null && e.Child.Width > 0), "emoji glyph images have visible dimensions");
        emojiLabel.Text = "새 제목 🫧";
        Check(emojiLabel.EmojiInlines.Count() == 1 && emojiLabel.Text == "새 제목 🫧", "updated emoji text preserves its Unicode source");
        Check(backdrop.Source is null && !backgroundImages.Exists, "default background remains absent");
        foreach (var theme in new[] { "light", "dark" })
        foreach (var width in new[] { 430, 719, 720, 1000, 1500 })
        {
            app.State.Settings.Theme = theme; Width = width; Height = width >= 1500 ? 940 : 740;
            for (var page = 0; page < 5; page++)
            {
                Select(page); await Settle(); await Settle();
                Check(bottomNavigation.IsVisible == (width < 720), $"{theme}/{width}/{page}: compact bottom navigation");
                Check(capsule.ActualWidth <= ActualWidth - 20, $"{theme}/{width}/{page}: navigation fits");
                Check(content.ActualWidth <= scroll.ViewportWidth + 1, $"{theme}/{width}/{page}: page fits");
                foreach (var button in Descendants<Button>(content).Where(x => x.IsVisible && x.ActualWidth > 0))
                {
                    var x = button.TranslatePoint(new Point(), scroll).X;
                    Check(x >= -1 && x + button.ActualWidth <= scroll.ViewportWidth + 2, $"{theme}/{width}/{page}: button fits {button.Content as string ?? button.ToolTip as string}");
                }
                if (page == 4) Check(Descendants<MasonryPanel>(content).Count() == 1, "settings use responsive cards");
                if (page == 2) Check(Descendants<Button>(content).Any(x => Equals(x.Content, "과거 방송 추가")) && Descendants<Button>(content).Any(x => Equals(x.Content, "시간 수정")), "archive editing controls");
                if (width is 430 or 1000 or 1500) Capture(screenshots, $"mac-parity-{theme}-{width}-page-{page}");
            }
        }
        Width = 1000; Height = 740; app.State.Settings.Theme = "light";
        app.State.Settings.SidebarVisible = true; Select(1); await Settle(); Check(sidebar.IsVisible && !capsule.IsVisible, "desktop sidebar navigation");
        Width = 430; await Settle(); Check(!sidebar.IsVisible && bottomNavigation.IsVisible, "compact temporarily hides sidebar");
        Width = 1000; await Settle(); Check(sidebar.IsVisible, "desktop restores sidebar preference"); app.State.Settings.SidebarVisible = false;
        Select(0); ShowWidget(); await Settle(); Check(widget?.IsVisible == true, "Windows summary widget opens"); widget?.Close();
        var record = app.State.Broadcasts.Last();
        var input = Path.Combine(app.Store.DirectoryPath, "fixture.png"); Directory.CreateDirectory(app.Store.DirectoryPath);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(images["qa:chzzk"]));
        using (var stream = File.Create(input)) encoder.Save(stream);
        await app.SetThumbnail(record.Id, input); Check(app.Thumbnails.PathFor(record) is not null && app.Thumbnails.Load(record) is not null, "custom thumbnail saves and decodes");
        var exported = app.Thumbnails.Export([record], app.Store.DirectoryPath);
        Check(File.Exists(Path.Combine(exported, "thumbnails.json")), "thumbnail export includes manifest");
        record.ThumbnailFilename = null;
        Check(await app.Thumbnails.ImportBundle(exported, app.State) == 1 && app.Thumbnails.PathFor(record) is not null, "thumbnail bundle restores corresponding broadcast");
        var localThumbnail = app.Thumbnails.PathFor(record)!;
        var sharedThumbnail = record with { Id = "shared-thumbnail-fixture" };
        app.Thumbnails.RemoveUnreferenced([record], [sharedThumbnail]);
        Check(File.Exists(localThumbnail), "replay cleanup preserves a thumbnail referenced by another record");
        app.Thumbnails.RemoveUnreferenced([record], []);
        Check(!File.Exists(localThumbnail) && File.Exists(input) && File.Exists(Path.Combine(exported, "thumbnails.json")), "replay cleanup deletes only app thumbnail copy and preserves original and export");
        record.ThumbnailFilename = "../state.json"; Check(app.Thumbnails.PathFor(record) is null, "thumbnail path traversal rejected");
        app.Thumbnails.RemoveUnreferenced([record], []);
        Check(File.Exists(app.Store.StatePath), "replay cleanup rejects thumbnail traversal");
        var originalBroadcasts = app.State.Broadcasts;
        var now = DateTimeOffset.UtcNow;
        var deletionFixture = new Broadcast { Id = "vod:15326326", VideoId = "15326326", Title = "삭제 확인 QA 영상", PublishedAt = now.AddDays(-1), StartedAt = now.AddMinutes(-45), Seconds = 1800 };
        app.State.Broadcasts = [deletionFixture]; month = RecordCalendar.Month(Channel.Today); selectedDay = null; broadcastUndated = false;
        Select(2); await Settle();
        Check(Descendants<TextBlock>(content).Any(t => t.Text == deletionFixture.Title), "unavailable replay remains visible before confirmation");
        BroadcastAvailability.Observe(app.State, "15326326", ReplayAvailability.Unavailable, now);
        BroadcastAvailability.Observe(app.State, "15326326", ReplayAvailability.Unavailable, now.AddSeconds(app.State.Settings.RefreshSeconds));
        app.Save(); await Settle();
        Check(!Descendants<TextBlock>(content).Any(t => t.Text == deletionFixture.Title) && Policies.DailySeconds(app.State.Broadcasts).Count == 0, "confirmed unavailable replay disappears from rendered calendar and totals");
        app.State.Broadcasts = originalBroadcasts;
        Select(0); await Settle();
        return string.Join(Environment.NewLine, report);
    }
}
