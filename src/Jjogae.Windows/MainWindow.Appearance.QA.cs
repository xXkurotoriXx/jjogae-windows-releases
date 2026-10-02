using System.Security.Cryptography;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private async Task VerifyAppearance(Action<bool, string> check, string? screenshotDirectory)
    {
        if (!app.IsTest) throw new InvalidOperationException("Appearance checks require an isolated test profile.");
        await VerifyGlass(check, screenshotDirectory);
        Select(4); await Settle();
        check(backgroundOpacitySlider?.IsEnabled == true && backgroundRemoveButton?.IsEnabled == false, "background controls reflect no image selected");
        await CheckOpacityInput("no selected image");
        check(Descendants<DatePicker>(content).Any() && Descendants<CheckBox>(content).Any(c => Equals(c.Content, "멤버십 직접 입력")), "optional manual membership supplements web observation");
        app.State.Settings.YouTubeMemberActive = false;
        app.State.YouTubeWeb = new(true, false, null, null, null, DateTimeOffset.UtcNow); app.Save(); await Settle();
        check(youtubeMembership?.Text == "이용하지 않음", "web observation updates visible settings immediately");
        foreach (var theme in new[] { "light", "dark" })
        {
            app.State.Settings.Theme = theme; Select(0); await Settle();
            CheckMembershipFlow(theme);
            check(overviewMetrics!.TranslatePoint(new Point(0, overviewMetrics.ActualHeight), scroll).Y <= scroll.ViewportHeight + 2, theme + " / inactive membership summary fits viewport before extra media"); Capture(screenshotDirectory, "home-no-membership-" + theme);
        }
        Width = 920; Height = 720; Select(0); await Settle(); CheckMembershipFlow("narrow");
        Capture(screenshotDirectory, "home-no-membership-narrow");
        app.State.Settings.SidebarVisible = true; Render(); await Settle(); CheckMembershipFlow("sidebar");
        Capture(screenshotDirectory, "home-no-membership-sidebar");
        app.State.Settings.SidebarVisible = false; Width = 1200; Height = 860;
        var subscribedAccount = app.State.Account!;
        app.State.Account = subscribedAccount with { IsSubscribed = false };
        foreach (var theme in new[] { "light", "dark" })
        {
            app.State.Settings.Theme = theme; Select(0); await Settle(); CheckMembershipFlow(theme + " / no subscriptions", false);
            Capture(screenshotDirectory, "home-no-subscriptions-" + theme);
        }
        Width = 920; Height = 720; app.State.Settings.SidebarVisible = true; Render(); await Settle(); CheckMembershipFlow("narrow sidebar / no subscriptions", false);
        Capture(screenshotDirectory, "home-no-subscriptions-sidebar");
        app.State.Account = null; Render(); await Settle(); CheckMembershipFlow("logged out", false);
        app.State.YouTubeWeb = new(true, true, 6, "루파", "10월 1일", DateTimeOffset.UtcNow); Render(); await Settle();
        check(overviewMetrics?.Children.Count == 7 && !Descendants<TextBlock>(content).Any(t => t.Text == "치지직 구독") && Descendants<TextBlock>(content).Any(t => t.Text == "YouTube 멤버십"), "YouTube membership remains independent of CHZZK subscription");
        app.State.Account = subscribedAccount; app.State.YouTubeWeb = null;
        Width = 1200; Height = 860; app.State.Settings.SidebarVisible = false;
        Select(4); await Settle();
        app.State.YouTubeWeb = new(true, true, 6, "루파", "10월 1일", DateTimeOffset.UtcNow); Select(0); await Settle();
        check(overviewMetrics?.Children.Count == 8 && Descendants<TextBlock>(content).Any(t => t.Text == "YouTube 멤버십"), "membership card returns in its original position");
        var logos = Descendants<Image>(overviewMetrics!).Where(i => i.Tag is string).ToArray();
        check(logos.Length == 6 && logos.Count(i => Equals(i.Tag, "chzzk")) == 3 && logos.Count(i => Equals(i.Tag, "youtube")) == 2 && logos.Count(i => Equals(i.Tag, "naver-cafe")) == 1 && logos.All(i => i.Source is BitmapImage && i.Stretch == Stretch.Uniform), "service logos loaded in their matching home cards without distortion");
        Select(4); await Settle();
        app.State.YouTubeWeb = new(true, false, null, null, null, DateTimeOffset.UtcNow); app.Save();

        var fixture = AppearanceFixture(Color.FromRgb(73, 125, 162), Color.FromRgb(130, 194, 182));
        var sourcePath = Path.Combine(app.Store.DirectoryPath, "qa-background-source.png");
        WriteFixture(sourcePath, fixture, new PngBitmapEncoder());
        var originalHash = SHA256.HashData(File.ReadAllBytes(sourcePath));
        await SetBackgroundImage(sourcePath); await Settle();
        check(backdrop.Source is BitmapImage { IsFrozen: true } && backgroundImages.Exists && backgroundOpacitySlider?.IsEnabled == true, "selected background is decoded and enables controls");
        check(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(sourcePath))), "background selection leaves original bytes unchanged");
        var copyHash = SHA256.HashData(File.ReadAllBytes(backgroundImages.ImagePath));
        var movedSource = sourcePath + ".moved"; File.Move(sourcePath, movedSource);
        backdrop.Source = null; await RestoreBackground(); await Settle();
        check(backdrop.Source is not null && copyHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(backgroundImages.ImagePath))), "saved background reloads after original is moved");
        foreach (var theme in new[] { "light", "dark" })
        {
            app.State.Settings.Theme = theme;
            Select(4); await Settle(); await CheckOpacityInput(theme + " / selected image");
            var pixels = new Dictionary<int, byte[]>();
            foreach (var percent in new[] { 0, 25, 50, 75, 100 })
            {
                Select(4); await Settle();
                var slider = Descendants<Slider>(content).Single(); slider.Value = percent;
                check(slider.IsMoveToPointEnabled && Math.Abs(backdrop.Opacity - percent / 100d) < .00001 && backgroundOpacityText?.Text == percent + "%", theme + $" / opacity {percent}% matches slider and image");
                Select(0); await Settle(); pixels[percent] = AppearancePixel();
                if (percent is 0 or 50 or 100) Capture(screenshotDirectory, $"background-{theme}-{percent}");
            }
            var surface = ((SolidColorBrush)root.Background).Color;
            check(Math.Abs(pixels[0][0] - surface.B * surface.A / 255d) <= 1 && Math.Abs(pixels[0][1] - surface.G * surface.A / 255d) <= 1 && Math.Abs(pixels[0][2] - surface.R * surface.A / 255d) <= 1 && pixels[0][3] == surface.A, theme + " / zero opacity preserves the theme tint and native backdrop alpha " + string.Join(',', pixels[0]) + " expected " + surface);
            check(Math.Abs(pixels[100][0] - 162) <= 1 && Math.Abs(pixels[100][1] - 125) <= 1 && Math.Abs(pixels[100][2] - 73) <= 1, theme + " / full opacity has no fixed color wash " + string.Join(',', pixels[100]));
            check(Enumerable.Range(0, 3).All(channel => new[] { 25, 50, 75 }.All(percent => Math.Abs(pixels[percent][channel] - (pixels[0][channel] * (1 - percent / 100d) + pixels[100][channel] * percent / 100d)) <= 3)), theme + " / rendered pixels follow the opacity percentage");
        }
        Select(4); await Settle();
        backgroundOpacitySlider!.Value = 37;
        await Task.Delay(400);
        check(Math.Abs(app.Store.Load().Settings.BackgroundOpacity - .37) < .00001, "opacity saves after keyboard or programmatic adjustment without losing focus");
        check(backgroundOpacityText?.Text == "37%", "opacity percentage remains updated after save");
        var selectTop = backgroundChooseButton!.TranslatePoint(new Point(), content).Y;
        scroll.ScrollToVerticalOffset(Math.Max(0, selectTop - 150)); await Settle(); Capture(screenshotDirectory, "settings-custom-background");

        var invalid = Path.Combine(app.Store.DirectoryPath, "qa-invalid.png"); File.WriteAllBytes(invalid, [0, 1, 2, 3]);
        await Reject(invalid, "invalid image keeps previous background");
        var oversized = Path.Combine(app.Store.DirectoryPath, "qa-oversized.jpg");
        using (var stream = File.Create(oversized)) stream.SetLength(BackgroundImageStore.MaximumBytes + 1);
        await Reject(oversized, "oversized image rejected before decoding");
        var unsupported = Path.Combine(app.Store.DirectoryPath, "qa-background.txt"); File.WriteAllBytes(unsupported, [0]);
        await Reject(unsupported, "unsupported background format rejected");
        var wide = Path.Combine(app.Store.DirectoryPath, "qa-wide.png");
        WriteFixture(wide, BitmapSource.Create(16385, 1, 96, 96, PixelFormats.Bgra32, null, new byte[16385 * 4], 16385 * 4), new PngBitmapEncoder());
        await Reject(wide, "excessive image dimensions rejected");
        foreach (var (extension, encoder) in new (string, BitmapEncoder)[] { ("jpg", new JpegBitmapEncoder()), ("bmp", new BmpBitmapEncoder()) })
        {
            var path = Path.Combine(app.Store.DirectoryPath, "qa-background." + extension); WriteFixture(path, fixture, encoder);
            await SetBackgroundImage(path); await Settle(); check(backdrop.Source is not null && backgroundImages.Exists, extension + " background imports and replaces the stored copy");
        }
        var restoredSettings = app.Store.Load().Settings;
        var previousBroadcasts = app.State.Broadcasts.Count; var previousCheese = app.State.Cheese.Count;
        backgroundRemoveButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        check(backdrop.Source is null && !backgroundImages.Exists && backgroundOpacitySlider?.IsEnabled == true, "remove background clears only its copy and keeps opacity adjustable");
        check(originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(movedSource))), "remove background preserves selected original image");
        check(app.State.Cheese.Count == previousCheese && app.State.Broadcasts.Count == previousBroadcasts && app.Store.Load().Broadcasts.Count == previousBroadcasts, "background changes preserve cheese and broadcast records");
        check(app.Store.Load().YouTubeWeb?.MembershipActive == false && Math.Abs(restoredSettings.BackgroundOpacity - .37) < .00001, "web membership and opacity survive state reload");
        await RestoreBackground(); check(backdrop.Source is null, "removed background stays absent after reload");
        Select(0); await Settle(); Capture(screenshotDirectory, "home-final-no-background");

        async Task Reject(string path, string label)
        {
            var previous = backdrop.Source; var stored = SHA256.HashData(File.ReadAllBytes(backgroundImages.ImagePath)); var rejected = false;
            try { await SetBackgroundImage(path); } catch { rejected = true; }
            check(rejected && ReferenceEquals(previous, backdrop.Source) && stored.SequenceEqual(SHA256.HashData(File.ReadAllBytes(backgroundImages.ImagePath))), label);
        }
        async Task CheckOpacityInput(string label)
        {
            var slider = backgroundOpacitySlider!;
            slider.BringIntoView(); await Settle(); slider.ApplyTemplate();
            var track = (Track?)slider.Template.FindName("PART_Track", slider);
            check(slider.IsEnabled && track?.Thumb is not null, label + " / interactive slider enabled");
            if (track?.Thumb is not { } thumb) return;
            slider.Value = 100; await Settle();
            var right = thumb.TranslatePoint(new Point(), slider).X;
            thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            thumb.RaiseEvent(new DragDeltaEventArgs(-track.ActualWidth * .4, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            thumb.RaiseEvent(new DragCompletedEventArgs(-track.ActualWidth * .4, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            await Settle();
            check(slider.Value is > 0 and < 100 && thumb.TranslatePoint(new Point(), slider).X < right, label + " / thumb drag moves away from 100 percent");
            check(Math.Abs(backdrop.Opacity - slider.Value / 100) < .00001, label + " / thumb drag updates image opacity");
            Slider.MinimizeValue.Execute(null, slider); await Settle();
            thumb.RaiseEvent(new DragDeltaEventArgs(track.ActualWidth * .3, 0) { RoutedEvent = Thumb.DragDeltaEvent }); await Settle();
            check(slider.Value is > 0 and < 100, label + " / thumb drag moves away from zero percent");
            var before = slider.Value; Slider.IncreaseSmall.Execute(null, slider); await Settle();
            check(slider.Value == before + 1, label + " / keyboard increment command changes value");
            var point = thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2), root);
            var hit = root.InputHitTest(point) as DependencyObject;
            while (hit is not null && !ReferenceEquals(hit, thumb)) hit = VisualTreeHelper.GetParent(hit);
            check(ReferenceEquals(hit, thumb), label + " / thumb is reachable by pointer hit testing");
            await Task.Delay(400);
            check(Math.Abs(app.Store.Load().Settings.BackgroundOpacity - slider.Value / 100) < .00001, label + " / dragged value persists without replacing the slider");
            var saved = slider.Value; Select(0); Select(4); await Settle();
            check(Math.Abs(backgroundOpacitySlider!.Value - saved) < .00001, label + " / dragged value survives page reentry");
        }
        void CheckMembershipFlow(string label, bool chzzkSubscribed = true)
        {
            var cards = overviewMetrics!.Children.OfType<FrameworkElement>().ToArray();
            check(cards.Length == (chzzkSubscribed ? 7 : 6) && !Descendants<TextBlock>(content).Any(t => t.Text == "YouTube 멤버십") && Descendants<TextBlock>(content).Any(t => t.Text == "치지직 구독") == chzzkSubscribed, label + " / inactive subscription and membership cards omitted");
            var expectedRows = WindowLayout.BalancedRows(cards.Length, overviewColumns).SelectMany((count, row) => Enumerable.Repeat(row, count)).ToArray();
            check(cards.Select((card, index) => Grid.GetRow(card) == expectedRows[index]).All(x => x), label + " / remaining cards shift forward without a middle gap");
            var last = cards.Where(c => Grid.GetRow(c) == Grid.GetRow(cards[^1])).ToArray();
            // Layout rounding distributes an indivisible row width with at most one physical pixel of difference.
            var pixel = 1 / VisualTreeHelper.GetDpi(overviewMetrics).DpiScaleX;
            var widthDifference = last.Max(c => c.ActualWidth) - last.Min(c => c.ActualWidth);
            var edgeDifference = Math.Abs(last[^1].TranslatePoint(new Point(last[^1].ActualWidth, 0), overviewMetrics).X + last[^1].Margin.Right - overviewMetrics.ActualWidth);
            check(last.Sum(Grid.GetColumnSpan) == overviewMetrics.ColumnDefinitions.Count && widthDifference <= pixel + .001 && edgeDifference <= pixel + .001, label + $" / last row distributes equal widths across the available space (width delta {widthDifference:0.###}, edge delta {edgeDifference:0.###} DIP)");
        }
    }

    private byte[] AppearancePixel()
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(4, 100, 1, 1), pixel, 4, 0); return pixel;
    }

    private static BitmapImage AppearanceFixture(Color background, Color accentColor, int width = 640, int height = 360)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            drawing.DrawEllipse(new SolidColorBrush(accentColor), null, new Point(width * .8, height * .42), width * .16, height * .28);
            drawing.DrawRectangle(new SolidColorBrush(accentColor) { Opacity = .25 }, null, new Rect(0, height * .7, width, height * .3));
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = new MemoryStream(); encoder.Save(stream); stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }

    private static void WriteFixture(string path, BitmapSource image, BitmapEncoder encoder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
