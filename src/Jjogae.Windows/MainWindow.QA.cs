using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    public async Task<string> VerifyCleanLaunch(string? screenshotDirectory = null)
    {
        if (!app.IsTest) throw new InvalidOperationException("Visual checks require an isolated test profile.");
        var lines = new List<string>();
        void Check(bool pass, string name) => lines.Add((pass ? "PASS: " : "FAIL: ") + name);
        void CheckVideoButtons(string label)
        {
            var buttons = Descendants<Button>(content).Where(b => Equals(b.Content, "영상 열기")).ToArray();
            Check(buttons.Length == 2 && buttons.All(b => Math.Abs(b.ActualWidth - 88) < .1 && Math.Abs(b.ActualHeight - 36) < .1), label + " / matching 88 x 36 video buttons");
            var hero = Descendants<Border>(content).Single(b => Equals(b.Tag, "home-media:치지직"));
            var youtube = Descendants<Border>(content).Single(b => Equals(b.Tag, "home-media:YouTube"));
            Check(hero.TranslatePoint(new Point(), content).Y < overviewMetrics!.TranslatePoint(new Point(), content).Y
                && youtube.TranslatePoint(new Point(), content).Y > overviewMetrics.TranslatePoint(new Point(), content).Y,
                label + " / Chzzk hero precedes summary and YouTube follows summary");
        }
        Check(app.State.Account is null && app.State.Cheese.Count == 0 && app.State.Cafe.Count == 0 && app.State.Broadcasts.Count == 0 && app.State.Settings.YouTubeSubscribedOn is null, "clean launch");
        Check(sections.Length == 5 && !sections.Contains("활동 기록"), "activity history removed from navigation and sidebar");
        Check(sidebar.Visibility == Visibility.Collapsed, "sidebar hidden by default");
        var installationFixture = new AppInstallation(Path.Combine(app.Store.DirectoryPath, "installation-fixture"));
        installationFixture.Load();
        Check(!installationFixture.StartupChoiceMade && installationFixture.AutomaticUpdates, "first launch asks startup choice and weekly updates default on");
        installationFixture.StartupChoiceMade = true; installationFixture.AutomaticUpdates = false; installationFixture.LastUpdateCheck = DateTimeOffset.UtcNow; installationFixture.Save();
        var restoredInstallation = new AppInstallation(Path.Combine(app.Store.DirectoryPath, "installation-fixture")); restoredInstallation.Load();
        Check(restoredInstallation.StartupChoiceMade && !restoredInstallation.AutomaticUpdates && restoredInstallation.LastUpdateCheck == installationFixture.LastUpdateCheck, "startup decision and update schedule survive restart independently of backup data");
        Check(AppInstallation.StartupCommand(@"C:\Test Folder\JjogaeStatus.exe") == "\"C:\\Test Folder\\JjogaeStatus.exe\" --startup", "startup command safely quotes paths with spaces");
        VerifyStartupRegistration(Check);
        await VerifyPublicUpdates(Check);
        VerifyStorageMaintenance(Check);
        var toastEntry = new Notification("fixture", "카페", "새 공지", "<공지> & 이모지 🫧❤️", DateTimeOffset.Now, DateTimeOffset.Now);
        var toastXml = System.Xml.Linq.XDocument.Parse(WindowsNotifications.Content(toastEntry).GetContent());
        Check(toastXml.Descendants("text").Any(x => x.Value == toastEntry.Title), "toast XML preserves emoji and escapes article text");
        Check((string?)toastXml.Root?.Attribute("launch") == "section=cafe", "toast activation opens cafe without untrusted commands or links");
        Check(backdrop.Source is null && !backgroundImages.Exists, "no bundled or selected background on clean launch");
        app.State.Settings.Theme = "light";
        Select(0); await Settle(); Capture(screenshotDirectory, "home-empty-light");
        SeedVisualChecks();
        ModelChanged();
        foreach (var theme in new[] { "light", "dark" })
        {
            app.State.Settings.Theme = theme;
            for (var index = 0; index < sections.Length; index++)
            {
                ((Button)navigation.Children[index]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
                Check(content.Children.Count >= 2, theme + " / " + sections[index]);
                Check(scroll.VerticalOffset == 0, theme + " / navigation resets scroll");
                if (index == 0) { Check(overviewMetrics!.TranslatePoint(new Point(0, overviewMetrics.ActualHeight), scroll).Y <= scroll.ViewportHeight + 2, theme + " / home summary fits viewport before extra media"); CheckVideoButtons(theme); }
                if (index is 1 or 2)
                {
                    var cells = Descendants<Button>(content).Count(b => b.Tag is DateOnly);
                    Check(cells == DateTime.DaysInMonth(Channel.Today.Year, Channel.Today.Month), theme + " / native month calendar days");
                    Check(Descendants<System.Windows.Controls.Primitives.UniformGrid>(content).Any(g => Equals(g.Tag, "record-calendar") && g.Children.Count == 49), theme + " / stable six-week calendar grid");
                }
                if (index == 1)
                {
                    Check(Descendants<Border>(content).Count(b => Equals(b.Tag, "cheese-bar")) == 14, theme + " / fourteen chart bars");
                    Check(Descendants<Border>(content).Count(b => Equals(b.Tag, "cheese-record")) == 10, theme + " / ten cheese records by default");
                }
                if (index == 3)
                {
                    var rows = Descendants<Border>(content).Where(b => b.Tag is string tag && tag.StartsWith("cafe-")).ToArray();
                    Check(rows.Length == 1 && !Descendants<TextBlock>(content).Any(t => t.Text == "오늘도 함께해줘서 고마워!"), theme + " / cafe shows notices only");
                    Check(rows.All(b => b.ActualHeight >= 76 && b.ActualHeight < 105 && b.BorderThickness.Left == 3), theme + " / compact cafe row proportions");
                    Check(rows.All(b => Math.Abs(b.ActualWidth - rows[0].ActualWidth) < 1), theme + " / aligned cafe row widths");
                }
                if (index == 4)
                {
                    Check(settingsLoginButton?.Visibility == Visibility.Visible && Equals(settingsLoginButton.Content, "로그인 브라우저 열기") && settingsLogoutButton?.Visibility == Visibility.Visible,
                        theme + " / logged-in settings can reopen the browser and still offer logout");
                    Check(!Descendants<Button>(content).Any(b => Equals(b.Content, "알림 테스트")) && notificationStatus is not null, theme + " / notification test removed and delivery status available");
                    Check(Descendants<Button>(content).Any(b => Equals(b.Content, "업데이트 확인")) && updateStatus is not null, theme + " / update check and status available");
                }
                foreach (var frame in mediaFrames)
                    Check(frame.ActualWidth > 0 && Math.Abs(frame.ActualWidth / frame.ActualHeight - AspectFrame.VideoRatio) < .02, theme + " / media aspect ratio");
                Capture(screenshotDirectory, $"page-{index}-{theme}");
                if (index == 2) { scroll.ScrollToBottom(); await Settle(); Capture(screenshotDirectory, "broadcast-records-" + theme); Check(!Descendants<Button>(content).Any(b => Equals(b.Content, "저장") || Equals(b.Content, "변경")), theme + " / thumbnail storage controls removed"); }
                if (index == 4) { scroll.ScrollToBottom(); await Settle(); Capture(screenshotDirectory, "settings-bottom-" + theme); }
            }
        }
        app.State.Settings.Theme = "light"; Width = 920; Height = 720; Select(0); await Settle();
        Check(navigation.ActualWidth <= capsule.ActualWidth, "navigation fits narrow window");
        Check(overviewMetrics!.TranslatePoint(new Point(0, overviewMetrics.ActualHeight), scroll).Y <= scroll.ViewportHeight + 2, "home summary fits 920 x 720 before extra media");
        foreach (var frame in mediaFrames) Check(Math.Abs(frame.ActualWidth / frame.ActualHeight - AspectFrame.VideoRatio) < .02, "narrow media aspect ratio");
        CheckVideoButtons("narrow");
        Capture(screenshotDirectory, "home-narrow");
        Select(3); await Settle(); Capture(screenshotDirectory, "cafe-narrow");
        Select(1); await Settle(); Check(recordPanelRight is not null && Grid.GetRow(recordPanelRight) == 0, "calendar and chart fit standard narrow width"); Capture(screenshotDirectory, "cheese-narrow");
        app.State.Settings.SidebarVisible = true; Render(); await Settle();
        Check(recordPanelRight is not null && Grid.GetRow(recordPanelRight) == 1, "calendar and chart stack with sidebar"); Capture(screenshotDirectory, "cheese-sidebar");
        Width = 1200; Height = 860; app.State.Settings.SidebarVisible = true; Select(0); await Settle(); Capture(screenshotDirectory, "home-sidebar");
        app.State.Settings.SidebarVisible = false; app.State.Settings.Transparency = 1; Select(0); await Settle();
        Check(backdrop.Opacity == 0, "transparent artwork"); Capture(screenshotDirectory, "home-transparent");
        app.State.Settings.Transparency = .6; Select(0); await Settle(); Capture(screenshotDirectory, "home-150dpi", 1.5);
        Check(Descendants<Button>(navigation).All(b => b.Template.FindName("Surface", b) is not null), "custom rounded navigation templates");
        lines.Add($"Rendered desktop: {root.ActualWidth:0} x {root.ActualHeight:0}; narrow: 920 x 720; additional 150% bitmap render.");
        Select(1); await Settle();
        var pageSize = Descendants<ComboBox>(content).Single(); pageSize.SelectedItem = "20개씩"; await Settle();
        Check(app.State.Settings.PageSize == 20 && Descendants<TextBlock>(content).Count(t => t.Text == "오늘 방송도 즐겁게 봤어요!") == 20, "cheese page size control");
        Descendants<ComboBox>(content).Single().SelectedItem = "10개씩"; await Settle();
        Descendants<Button>(content).Single(b => Equals(b.Content, "다음")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        Check(cheesePage == 1 && Descendants<TextBlock>(content).Count(t => t.Text == "오늘 방송도 즐겁게 봤어요!") == 10, "cheese pagination");
        Select(3); await Settle();
        Check(cafeReadSelected?.Visibility == Visibility.Collapsed, "cafe selection action hidden with no checks");
        var cafeSelection = Descendants<CheckBox>(content).First();
        cafeSelection.IsChecked = true; await Settle();
        Check(cafeReadSelected?.Visibility == Visibility.Visible && selectedCafe.Count == 1, "checking cafe article shows selection action");
        cafeSelection.IsChecked = false; await Settle();
        Check(cafeReadSelected?.Visibility == Visibility.Collapsed && selectedCafe.Count == 0, "unchecking last article hides selection action");
        cafeSelection.IsChecked = true; await Settle();
        cafeReadSelected!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        Check(cafeReadSelected?.Visibility == Visibility.Collapsed && selectedCafe.Count == 0, "reading selection clears selection action");
        recoveryExpanded = true; Render(); await Settle();
        var recoveryAt = new DateTimeOffset(Channel.Today.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(9));
        var recoveryArticles = new[] { new CafePost("recover-one", "복구할 공지", Channel.Name, "공지", true, recoveryAt, Channel.CafeUrl), new CafePost("recover-two", "복구할 루파 글", Channel.Name, "자유게시판", false, recoveryAt, Channel.CafeUrl) };
        var previousCafeCount = app.State.Cafe.Count;
        app.SeedRecoveryCheck(new(new(Channel.Today.AddDays(-6), Channel.Today, CafeHistoryScope.Notice), recoveryArticles, 1, 2, "")); Render(); await Settle();
        Check(app.State.Cafe.Count == previousCafeCount, "preview preserves cafe collection");
        selectedRecovery.Add("recover-one"); Render(); await Settle(); Capture(screenshotDirectory, "cafe-recovery");
        var recoveryToggle = Descendants<Button>(content).Single(b => Equals(b.Tag, "recovery-toggle"));
        var queryButton = Descendants<Button>(content).Single(b => Equals(b.Content, "조회"));
        var queryTop = queryButton.TranslatePoint(new Point(), root).Y;
        Check(queryButton.ActualHeight >= 36 && queryTop >= recoveryToggle.TranslatePoint(new Point(), root).Y + recoveryToggle.ActualHeight && queryTop + queryButton.ActualHeight <= scroll.TranslatePoint(new Point(), root).Y + scroll.ActualHeight, "cafe recovery query controls visible below header");
        Check(!Descendants<ComboBox>(content).Any(c => c.Items.Contains("전체 글")) && Descendants<ComboBox>(content).Any(c => c.Items.Contains("최근 7일")), "cafe recovery limited to notices with date range controls");
        app.State.Settings.Theme = "dark"; Render(); await Settle(); Capture(screenshotDirectory, "cafe-recovery-dark");
        app.State.Settings.Theme = "light"; Render(); await Settle();
        Descendants<Button>(content).Single(b => b.Content is string label && label.StartsWith("선택 복구")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        Check(app.State.Cafe.Any(x => x.Id == "recover-one") && app.State.Cafe.All(x => x.Id != "recover-two"), "UI restores selected cafe article only");
        Check(app.State.Pending.Count == 0, "restoration creates no historical alerts");
        Descendants<ComboBox>(content).Single(c => c.Items.Contains("직접 선택")).SelectedItem = "직접 선택"; await Settle();
        Check(Descendants<DatePicker>(content).Count() == 2 && app.Recovery is null, "custom cafe range exposes both dates and clears stale preview"); Capture(screenshotDirectory, "cafe-recovery-custom");
        Descendants<Button>(content).Single(b => Equals(b.Tag, "recovery-toggle")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        Check(!recoveryExpanded && !Descendants<Button>(content).Any(b => Equals(b.Content, "조회")) && selectedRecovery.Count == 0, "cafe recovery collapses without retaining controls or selection");
        Select(4); await Settle();
        app.State.YouTubeWeb = null; app.State.Settings.YouTubeWebEnabled = true; ModelChanged(); await Settle();
        Check(youtubeConnect?.Visibility == Visibility.Visible && youtubeDisconnect?.Visibility == Visibility.Collapsed, "opening YouTube login does not imply authenticated session");
        app.State.YouTubeWeb = new(null, null, null, null, null, DateTimeOffset.UtcNow); ModelChanged(); await Settle();
        Check(youtubeConnect?.Visibility == Visibility.Collapsed && youtubeDisconnect?.Visibility == Visibility.Visible, "login-only observation switches to logout immediately");
        app.State.YouTubeWeb = app.State.YouTubeWeb.Merge(new(true, null, null, null, null, DateTimeOffset.UtcNow)); ModelChanged(); await Settle();
        Check(youtubeRelationship?.Text == "구독 중", "subscription result updates existing settings controls");
        youtubeDisconnect!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        Check(app.State.YouTubeWeb is null && !app.State.Settings.YouTubeWebEnabled && youtubeConnect?.Visibility == Visibility.Visible && youtubeDisconnect?.Visibility == Visibility.Collapsed, "YouTube logout clears state and restores login");
        var cleanup = new TaskCompletionSource();
        app.YouTubeCleanupTest = () => cleanup.Task;
        app.State.Settings.YouTubeWebEnabled = true; app.State.YouTubeWeb = new(true, null, null, null, null, DateTimeOffset.UtcNow); ModelChanged();
        youtubeDisconnect!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        Check(youtubeConnect?.Visibility == Visibility.Visible && youtubeDisconnect.Visibility == Visibility.Collapsed && app.State.YouTubeWeb is null && app.State.Settings.YouTubeResetPending, "login returns before delayed browser cleanup finishes");
        cleanup.SetResult(); await Settle();
        Check(youtubeConnect!.IsEnabled && !app.State.Settings.YouTubeResetPending, "login enabled after delayed cleanup");
        app.YouTubeCleanupTest = () => Task.FromException(new InvalidOperationException("fixture cleanup failure"));
        await app.DisconnectYouTube(); await Settle();
        Check(youtubeConnect.Visibility == Visibility.Visible && youtubeConnect.IsEnabled && app.State.Settings.YouTubeResetPending && app.State.YouTubeWeb is null, "failed cleanup remains disconnected and requires cleanup before reconnect");
        app.YouTubeCleanupTest = null; app.State.Settings.YouTubeResetPending = false;
        var signedIn = app.State.Account;
        var savedChatProfile = app.State.ChatProfile;
        var settingsSlider = backgroundOpacitySlider;
        app.State.Account = null; ModelChanged(); await Settle();
        Check(settingsLoginButton?.Visibility == Visibility.Visible && Equals(settingsLoginButton.Content, "로그인") && settingsLogoutButton?.Visibility == Visibility.Collapsed, "logged-out settings show login only");
        Check(ReferenceEquals(settingsSlider, backgroundOpacitySlider), "login status update preserves current settings controls");
        Capture(screenshotDirectory, "settings-logged-out");
        app.State.Account = signedIn; app.State.YouTubeSubscribers = 6790; ModelChanged(); await Settle();
        Check(settingsLoginButton?.Visibility == Visibility.Visible && Equals(settingsLoginButton.Content, "로그인 브라우저 열기") && settingsLogoutButton?.Visibility == Visibility.Visible,
            "login response keeps browser confirmation available without hiding logout");
        Check(youtubeSubscribersLabel?.Text == "6,790명" && ReferenceEquals(settingsSlider, backgroundOpacitySlider), "automatic subscriber update preserves settings editor");
        Capture(screenshotDirectory, "settings-logged-in");
        Select(0); await Settle(); loginButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        Check(accountPopup?.IsOpen == true && section == 0, "logged-in header opens channel chat profile");
        Check(accountPopup is not null && Descendants<TextBlock>(accountPopup.Child).Any(t => t.Text == "12개월"), "chat subscription months visible");
        if (accountPopup?.Child is FrameworkElement profileView && screenshotDirectory is not null)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(profileView.ActualWidth), (int)Math.Ceiling(profileView.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(profileView);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(screenshotDirectory, "chat-profile.png")); encoder.Save(stream);
        }
        Descendants<Button>(accountPopup!.Child).Single(b => Equals(b.Content, "계정 설정")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        Check(section == 4 && !accountPopup.IsOpen, "chat profile account settings action");
        app.State.ChatProfile = null; UpdateAccountChip(); Select(0); await Settle();
        loginButton!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        Check(accountPopup?.IsOpen == true && Descendants<TextBlock>(accountPopup!.Child).Any(t => t.Text.Contains("채팅 프로필을 아직 확인하지 못했습니다"))
            && Descendants<Button>(accountPopup.Child).Any(b => Equals(b.Content, "로그인 브라우저 열기")),
            "missing chat profile offers a direct route to browser confirmation");
        if (accountPopup?.Child is FrameworkElement retryView && screenshotDirectory is not null)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(retryView.ActualWidth), (int)Math.Ceiling(retryView.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(retryView);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(screenshotDirectory, "chat-profile-retry.png")); encoder.Save(stream);
        }
        accountPopup!.IsOpen = false; app.State.ChatProfile = savedChatProfile;
        UpdateAccountChip(); Select(4); await Settle();
        Descendants<ComboBox>(content).Single(c => c.Items.Contains("다크")).SelectedItem = "다크"; await Settle();
        Check(dark && app.State.Settings.Theme == "dark", "theme setting control");
        Descendants<ComboBox>(content).Single(c => c.Items.Contains("5분")).SelectedItem = "5분"; await Settle();
        Check(app.State.Settings.RefreshSeconds == 300, "refresh interval control");
        await VerifyAppearance(Check, screenshotDirectory);
        await VerifyCompactLayout(Check, screenshotDirectory);
        try { await VerifyYouTubeWebView(Check); }
        catch (Exception error) { lines.Add("FAIL: " + error); }
        var restoredLogin = new LoginWindow(Path.Combine(app.Store.DirectoryPath, "naver-restore-test"), () => Task.CompletedTask);
        try
        {
            var ready = await restoredLogin.RestoreSession(core =>
            {
                var cookie = core.CookieManager.CreateCookie("JjogaeFixture", "synthetic", ".naver.com", "/");
                core.CookieManager.AddOrUpdateCookie(cookie);
            }).WaitAsync(TimeSpan.FromSeconds(20));
            Check(ready && !restoredLogin.IsVisible && !restoredLogin.Ready, "Naver profile restores without showing or loading the WPF login view");
            Check((await restoredLogin.Cookies(new Uri(Channel.Url))).Contains("JjogaeFixture=synthetic"), "background restoration supplies the app profile cookies to API requests");
            Check(await restoredLogin.Cookies(new Uri("https://example.com")) == "", "restored cookies never supplied to another service");
            await restoredLogin.SignOut();
            Check(await restoredLogin.Cookies(new Uri(Channel.Url)) == "", "logout clears a restored session without opening the login window");
        }
        catch (Exception error) { lines.Add("FAIL: background Naver restoration: " + error); }
        finally { restoredLogin.ShutDown(); }
        if (app.State.Account is { } originalAccount)
        {
            app.State.Account = originalAccount with { Nickname = "짧음" }; UpdateAccountChip(); await Settle();
            var shortWidth = loginButton!.ActualWidth;
            app.State.Account = originalAccount with { Nickname = "아주긴닉네임을사용하는계정의전체이름" }; UpdateAccountChip(); await Settle();
            var longName = Descendants<TextBlock>(loginButton).Single(block => Equals(block.Tag, "account-name"));
            Check(loginButton.ActualWidth > shortWidth && loginButton.ActualWidth <= loginButton.MaxWidth, "account chip sizes to nickname within maximum");
            Check(longName.TextTrimming == TextTrimming.CharacterEllipsis && longName.ActualWidth <= 81 && loginButton.ToolTip!.ToString()!.Contains("아주긴닉네임"), "long nickname ellipsis keeps full name in tooltip");
            Capture(screenshotDirectory, "long-nickname");
            app.State.Account = originalAccount; UpdateAccountChip();
        }
        var didClose = false;
        Closed += (_, _) => didClose = true;
        Descendants<Button>(header).Single(button => Equals(button.ToolTip, "닫기 · 백그라운드 실행")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
        Check(!didClose && WindowState == WindowState.Minimized && ShowInTaskbar && !backgroundClosed, "close minimizes to taskbar without disposing background services");
        WindowState = WindowState.Normal; Activate(); await Settle();
        Check(IsVisible && !didClose && Title == "쪼개 상황실", "background window restores with correct app title");
        var closeProbe = new MainWindow(app);
        var systemCloseCompleted = false;
        closeProbe.Closed += (_, _) => systemCloseCompleted = true;
        closeProbe.Show(); await Settle();
        System.Windows.SystemCommands.CloseWindow(closeProbe); await Settle();
        Check(systemCloseCompleted && closeProbe.backgroundClosed, "system taskbar close disposes window instead of minimizing");
        return string.Join(Environment.NewLine, lines);
    }

    private async Task VerifyCompactLayout(Action<bool, string> check, string? screenshots)
    {
        check(MinWidth == 430 && MinHeight == 540, "compact window minimum 430 x 540");
        app.State.Media.Add(new Media("clip:qa-latest", "치지직", "클립", "최신 클립 · 길어진 제목도 좁은 창에서 읽을 수 있습니다", "https://chzzk.naver.com/clips/qa-latest", "qa:chzzk", DateTimeOffset.UtcNow));
        foreach (var (width, height) in new[] { (720, 540), (854, 600), (1024, 640) })
        foreach (var showSidebar in new[] { false, true })
        {
            Width = width; Height = height; app.State.Settings.SidebarVisible = showSidebar;
            for (var page = 0; page < sections.Length; page++)
            {
                Select(page); await Settle(); await Settle();
                check(scroll.ExtentWidth <= scroll.ViewportWidth + 1, $"compact {width} / sidebar {showSidebar} / {page} no horizontal overflow");
                var navRight = capsule.TranslatePoint(new Point(capsule.ActualWidth, 0), headerBar).X;
                var navLeft = capsule.TranslatePoint(new Point(), headerBar).X;
                var profileRight = loginButton!.TranslatePoint(new Point(loginButton.ActualWidth, 0), headerBar).X;
                var caption = Descendants<StackPanel>(headerBar!).Single(panel => Equals(panel.Tag, "window-controls"));
                check(!capsule.IsVisible || (profileRight <= navLeft + 1 && navRight <= caption.TranslatePoint(new Point(), headerBar).X + 1), $"compact {width} / {page} header controls do not overlap");
                check(!capsule.IsVisible || Math.Abs((navLeft + navRight) / 2 - headerBar!.ActualWidth / 2) < 1, "navigation stays centered in window");
                check(refreshButton!.TranslatePoint(new Point(), headerBar).X < loginButton.TranslatePoint(new Point(), headerBar).X, "refresh and account stay on left");
                if (app.State.Account is not null)
                {
                    var accountName = Descendants<TextBlock>(loginButton).Single(block => Equals(block.Tag, "account-name"));
                    check(accountName.ActualWidth > 0 && accountName.ActualWidth <= 81 && accountName.TextTrimming == TextTrimming.CharacterEllipsis, "compact nickname is visible with bounded ellipsis");
                    check(!Descendants<Image>(loginButton).Any(), "compact account badges stay in profile popup");
                }
                foreach (var frame in mediaFrames)
                    check(frame.ActualHeight > 0 && Math.Abs(frame.ActualWidth / frame.ActualHeight - AspectFrame.VideoRatio) < .02, "compact thumbnail keeps 16:9 ratio");
                if (page == 0)
                {
                    check(Descendants<Button>(content).Count(button => button.Tag is string tag && tag.StartsWith("shortcut:")) == 4, "four channel shortcuts available");
                    check(Descendants<Button>(content).Any(button => Equals(button.Content, "클립 열기")), "latest clip is available separately from replay");
                }
                if (width == 720 && !showSidebar) Capture(screenshots, "compact-720-page-" + page);
                scroll.ScrollToBottom(); await Settle();
                if (width == 720 && !showSidebar && page == 0) Capture(screenshots, "compact-720-home-bottom");
                check(Math.Abs(scroll.ScrollableHeight - scroll.VerticalOffset) < 2, "compact bottom content reachable");
            }
        }
        app.State.Settings.SidebarVisible = false; Width = 720; Height = 720; Select(0); await Settle();
        var narrowThumbnailWidth = mediaFrames[0].ActualWidth;
        var narrowHeroWidth = Descendants<Border>(content).Single(b => Equals(b.Tag, "home-media:치지직")).ActualWidth;
        Width = 1000; await Settle();
        var wideHeroWidth = Descendants<Border>(content).Single(b => Equals(b.Tag, "home-media:치지직")).ActualWidth;
        var expectedGrowth = (wideHeroWidth - narrowHeroWidth) * .4;
        check(expectedGrowth > 50 && Math.Abs(mediaFrames[0].ActualWidth - narrowThumbnailWidth - expectedGrowth) < 2,
            $"hero thumbnail follows 40% of actual card width growth ({narrowThumbnailWidth:0.0} to {mediaFrames[0].ActualWidth:0.0}; expected growth {expectedGrowth:0.0})");
        check(homeMedia!.Columns == 2 && double.IsPositiveInfinity(homeMedia.MaxWidth), "latest media uses available width without height-based cap");
        Capture(screenshots, "home-wide-responsive");
        scroll.ScrollToBottom(); await Settle(); Capture(screenshots, "home-wide-media");
        Width = 1000; Height = 720; Select(0); await Settle();
    }

    private async Task VerifyYouTubeWebView(Action<bool, string> check)
    {
        var result = new TaskCompletionSource<YouTubeWebObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var popupReady = new TaskCompletionSource<Microsoft.Web.WebView2.Core.CoreWebView2>(TaskCreationOptions.RunContinuationsAsynchronously);
        var popupClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var phase = "initial page";
        var observationCount = 0;
        Microsoft.Web.WebView2.Core.CoreWebView2? browser = null;
        var window = new YouTubeWindow(Path.Combine(app.Store.DirectoryPath, "webview-fixture"), value =>
        {
            observationCount++;
            if (value.Subscribed == true) result.TrySetResult(value);
        }, core =>
        {
            var isPopup = browser is not null;
            browser ??= core;
            if (isPopup)
            {
                core.NavigationCompleted += (_, e) =>
                {
                    // Assigning NewWindow replaces the initial blank document.
                    // Only the fixture destination completes this assertion.
                    if (core.Source != "https://www.youtube.com/auth-fixture") return;
                    if (e.IsSuccess) popupReady.TrySetResult(core);
                    else popupReady.TrySetException(new InvalidOperationException($"Fixture popup navigation: {e.WebErrorStatus}; HTTP {e.HttpStatusCode}"));
                };
                core.WindowCloseRequested += (_, _) => popupClosed.TrySetResult();
            }
            core.AddWebResourceRequestedFilter("*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                const string html = """<html><body><ytd-c4-tabbed-header-renderer></ytd-c4-tabbed-header-renderer><yt-page-header-renderer><div>구독자 6.94천명</div><button aria-label="현재 설정은 모든 알림 수신입니다.">구독중</button></yt-page-header-renderer><main></main></body></html>""";
                e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(html)), 200, "OK", "Content-Type: text/html; charset=utf-8");
            };
            if (!isPopup) core.CookieManager.AddOrUpdateCookie(core.CookieManager.CreateCookie("fixture_session", "test", "www.youtube.com", "/"));
        });
        try
        {
            window.Owner = this; window.Show();
            var value = await result.Task.WaitAsync(TimeSpan.FromSeconds(20));
            check(value.Subscribed == true && value.Subscribers == 6940, "real WebView2 reads subscription and count beyond empty legacy header without avatar");
            check(browser!.Source == Channel.YouTubeUrl, "visible channel is not redirected by automatic reading");
            phase = "open authentication popup";
            await browser.ExecuteScriptAsync("window.open('https://www.youtube.com/auth-fixture', 'fixture-auth')");
            var popup = await popupReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
            check(await popup.ExecuteScriptAsync("window.opener !== null") == "true", "authentication popup preserves opener connection");
            check(browser.Source == Channel.YouTubeUrl, "authentication popup does not replace main document");
            var beforePopupRead = observationCount;
            await window.Refresh(true);
            check(beforePopupRead == observationCount, "automatic reading pauses while authentication popup is open");
            check((await popup.CookieManager.GetCookiesAsync("https://www.youtube.com")).Any(cookie => cookie.Name == "fixture_session"), "authentication popup shares app profile");
            phase = "close authentication popup";
            await popup.ExecuteScriptAsync("window.close()");
            await popupClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            phase = "regional Google account navigation";
            foreach (var address in new[] { "https://accounts.google.co.kr/fixture", "https://accounts.google.co.in/accounts/SetSID", "https://www.google.co.in/accounts/SetSID", Channel.YouTubeUrl })
            {
                var regionalReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Completed(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (browser.Source == address && e.IsSuccess) regionalReady.TrySetResult();
                }
                browser.NavigationCompleted += Completed;
                try
                {
                    browser.Navigate(address);
                    await regionalReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    check(browser.Source == address, "regional account continuation and YouTube return are not canceled");
                    if (!YouTubeWebPolicy.CanObserve(address))
                    {
                        var beforeAuthenticationRead = observationCount;
                        await window.Refresh(true);
                        check(observationCount == beforeAuthenticationRead, "authentication document is not read as YouTube data");
                    }
                }
                finally { browser.NavigationCompleted -= Completed; }
            }
            phase = "logout";
            await window.SignOut().WaitAsync(TimeSpan.FromSeconds(8));
            check(browser is not null && (await browser.CookieManager.GetCookiesAsync("https://www.youtube.com")).Count == 0, "real WebView2 logout clears isolated session cookies");
        }
        catch (Exception error)
        {
            var message = string.Join(" | ", Descendants<TextBlock>(window).Select(block => block.Text));
            throw new InvalidOperationException($"WebView fixture phase: {phase}; status: {message}", error);
        }
        finally { window.ShutDown(); }
    }

    private async Task Settle()
    {
        await Dispatcher.InvokeAsync(() => UpdateLayout(), DispatcherPriority.ApplicationIdle);
        await Task.Delay(60);
        UpdateLayout();
    }
    private void Capture(string? directory, string name, double scale = 1)
    {
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale), (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }

    private void SeedVisualChecks()
    {
        // Synthetic fixtures in an isolated smoke profile. Never loaded by a normal launch or written to the user's profile.
        var state = app.State;
        var at = new DateTimeOffset(Channel.Today.ToDateTime(new TimeOnly(19, 30)), TimeSpan.FromHours(9));
        state.Account = new Account("테스트", 12000, "아끼는 쪼개 티어", Channel.Today.AddDays(-250), "2026-10-06", new string('a', 32), IsSubscribed: true);
        state.ChatProfile = new(new string('a', 32), Channel.Id, [new("qa-sub", "subscription", "https://ssl.pstatic.net/qa.png", "구독 배지", "12개월")], 12, "아끼는 쪼개 티어", 3, at.AddDays(-250), at);
        state.Followers = 6200; state.YouTubeSubscribers = 6500; state.YouTubeCheckedAt = at; state.YouTubeLatestVideoId = "qa-y";
        state.Settings.YouTubeSubscribedOn = Channel.Today.AddDays(-150); state.Settings.YouTubeMemberSince = Channel.Today.AddDays(-60); state.Settings.YouTubeMemberActive = true;
        state.YouTubeWeb = new(true, true, 2, "루파", "10월 1일", DateTimeOffset.UtcNow);
        var light = "qa:chzzk"; var darkImage = "qa:youtube";
        images[light] = AppearanceFixture(Color.FromRgb(73, 125, 162), Color.FromRgb(130, 194, 182));
        images[darkImage] = AppearanceFixture(Color.FromRgb(69, 55, 99), Color.FromRgb(173, 141, 210));
        state.Media = [new Media("qa-c", "치지직", "다시보기", "오늘도 함께하는 루파의 방송 · 길어진 제목도 두 줄 안에서 정리하기", Channel.Url, light, at), new Media("qa-y", "YouTube", "새 영상", "새로운 이야기", Channel.YouTubeUrl, darkImage, at.AddDays(-1))];
        state.Cheese = Enumerable.Range(1, 48).Select(i => new Cheese("qa-" + i, Channel.Name, i * 100, "오늘 방송도 즐겁게 봤어요!", "후원", i <= 24 ? at.AddMinutes(-i) : at.AddDays(-(i - 24)))).ToList();
        state.Cafe = [new CafePost("1", "🧧 이번 주 방송 일정 안내 🫧 ❤️", Channel.Name, "공지", true, at, Channel.CafeUrl), new CafePost("2", "오늘도 함께해줘서 고마워!", Channel.Name, "자유게시판", false, at.AddHours(-1), Channel.CafeUrl), new CafePost("3", "오늘 방송의 재미있었던 순간들 · 길게 작성한 제목도 카드 밖으로 벗어나지 않고 읽을 수 있도록 정리한 테스트 글입니다", "테스트", "자유게시판", false, at.AddHours(-2), Channel.CafeUrl)];
        state.Broadcasts = Enumerable.Range(0, 6).Select(i => new Broadcast { Id = "qa-vod-" + i, Title = "함께한 루파의 방송 " + (i + 1), StartedAt = at.AddDays(-i), Seconds = 7200 + i * 1800, ImageUrl = i % 2 == 0 ? light : darkImage, Url = Channel.Url }).ToList();
    }
}
