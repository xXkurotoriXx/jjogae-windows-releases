using Microsoft.Win32;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private TextBlock? accountLabel;
    private TextBlock? youtubeSubscribersLabel;
    private Button? settingsLoginButton, settingsLogoutButton;
    private TextBlock? youtubeRelationship, youtubeMembership, youtubeTier, youtubeBilling, youtubeChecked;
    private Button? youtubeConnect, youtubeDisconnect;
    private TextBlock? notificationStatus;
    private void UpdateNotificationStatus()
    {
        if (notificationStatus is not null) notificationStatus.Text = windowsNotifications.Status;
    }
    private void SettingsPage()
    {
        var firstCard = content.Children.Count;
        var settings = app.State.Settings;
        accountLabel = Text(app.State.Account?.Nickname ?? "로그인하지 않음", 13, secondary: true);
        settingsLoginButton = Button("로그인", () => app.ShowLogin(this));
        settingsLogoutButton = Button("로그아웃", async () => await app.Logout());
        var account = Card(Column(Text("치지직 · 네이버", 17, true), SettingRow(accountLabel, Row(settingsLoginButton, settingsLogoutButton))));
        content.Children.Add(account);
        content.Children.Add(Card(SettingRow(Text("쪼개 상황실 · X 버튼은 백그라운드 실행"), Button("완전히 종료", ExitApplication))));

        youtubeSubscribersLabel = Text("", 16, true);
        var observation = app.State.YouTubeWeb;
        var relationship = observation?.Subscribed switch { true => "구독 중", false => "구독하지 않음", _ => "미확인" };
        var membership = observation?.MembershipActive switch { true => observation.MembershipMonths is { } months ? $"{months}개월" : "이용 중", false => "이용하지 않음", _ => "미확인" };
        youtubeRelationship = Text(relationship); youtubeMembership = Text(membership);
        youtubeTier = Text(observation?.Tier ?? "—"); youtubeBilling = Text(observation?.NextBillingLabel ?? "—");
        youtubeChecked = Text("", 12, secondary: true);
        youtubeConnect = Button("로그인", () => app.ShowYouTube(this));
        youtubeDisconnect = Button("로그아웃", async () =>
        {
            if (youtubeDisconnect is not { } logout) return;
            logout.IsEnabled = false;
            try { await app.DisconnectYouTube(); }
            catch (Exception) { MessageBox.Show("로그인 정보를 지우지 못했습니다. 다시 시도해 주세요.", "YouTube"); }
            finally { logout.IsEnabled = true; UpdateSettingsStatus(); }
        });
        content.Children.Add(Card(Column(Text("YouTube", 17, true),
            SettingRow(Text("채널 구독자 수"), youtubeSubscribersLabel),
            SettingRow(Text("웹 연결"), Row(youtubeConnect, youtubeDisconnect)),
            SettingRow(Text("내 구독"), youtubeRelationship),
            SettingRow(Text("내 멤버십"), youtubeMembership),
            SettingRow(Text("멤버십 등급"), youtubeTier),
            SettingRow(Text("다음 결제일"), youtubeBilling), youtubeChecked)));
        UpdateSettingsStatus();

        var themeLabels = new Dictionary<string, string> { ["system"] = "시스템 설정", ["light"] = "라이트", ["dark"] = "다크" };
        var theme = Choice(themeLabels.Values, themeLabels[settings.Theme], value => { settings.Theme = themeLabels.First(x => x.Value == value).Key; app.Save(); Render(); });
        backgroundOpacityText = Text($"{settings.BackgroundOpacity * 100:0}%", 12, secondary: true); backgroundOpacityText.Width = 44; backgroundOpacityText.TextAlignment = TextAlignment.Right;
        backgroundOpacitySlider = new Slider { Minimum = 0, Maximum = 100, Value = settings.BackgroundOpacity * 100, SmallChange = 1, LargeChange = 10, TickFrequency = 1, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Width = 180, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        System.Windows.Automation.AutomationProperties.SetName(backgroundOpacitySlider, "배경 불투명도");
        backgroundOpacitySlider.ValueChanged += (_, change) => UpdateBackgroundOpacity(change.NewValue);
        backgroundOpacitySlider.LostMouseCapture += (_, _) => FlushBackgroundPreferences(); backgroundOpacitySlider.LostKeyboardFocus += (_, _) => FlushBackgroundPreferences();
        backgroundChooseButton = Button("이미지 선택", ChooseBackground);
        backgroundRemoveButton = Button("배경 지우기", RemoveBackground);
        backgroundStatus = Text("", 12, secondary: true); backgroundStatus.Margin = new Thickness(0, 0, 12, 0);
        UpdateBackgroundControls();
        content.Children.Add(Card(Column(Text("화면", 17, true), SettingRow(Text("모드"), theme),
            SettingRow(Text("배경 이미지"), Row(backgroundStatus, backgroundChooseButton, backgroundRemoveButton)),
            SettingRow(Text("배경 불투명도"), Row(backgroundOpacitySlider, backgroundOpacityText)))));

        var refreshLabels = new Dictionary<int, string> { [60] = "1분", [300] = "5분", [600] = "10분", [3600] = "1시간" };
        var notifications = new CheckBox { IsChecked = settings.Notifications, Foreground = ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        System.Windows.Automation.AutomationProperties.SetName(notifications, "Windows 알림");
        notifications.Checked += (_, _) => { if (!building) app.SetNotifications(true); };
        notifications.Unchecked += (_, _) => { if (!building) app.SetNotifications(false); };
        notificationStatus = Text(windowsNotifications.Status, 12, secondary: true);
        notificationStatus.TextWrapping = TextWrapping.Wrap;
        content.Children.Add(Card(Column(Text("자동 동기화", 17, true),
            SettingRow(Text("새로고침 간격"), Choice(refreshLabels.Values, refreshLabels[settings.RefreshSeconds], value => { settings.RefreshSeconds = refreshLabels.First(x => x.Value == value).Key; app.Save(); })),
            SettingRow(Text("Windows 알림"), notifications),
            notificationStatus, Text("카페 알림은 네이버 연결이 필요합니다. 앱을 완전히 종료하거나 PC가 절전 상태이면 확인이 멈춥니다.", 11, secondary: true))));
        var storageTotal = Text("저장 용량 확인 중…", 14, true);
        var storageDetails = Text("", 11, secondary: true); storageDetails.TextWrapping = TextWrapping.Wrap;
        async Task ReadStorage()
        {
            var usage = await app.ReadStorageUsage();
            storageTotal.Text = $"저장된 데이터 {usage.Total / 1024d / 1024d:N1} MB";
            storageDetails.Text = $"기록 {usage.Records / 1024d / 1024d:N1} · 이미지 {usage.Images / 1024d / 1024d:N1} · 웹 데이터 {usage.Browser / 1024d / 1024d:N1} · 업데이트 {usage.Updates / 1024d / 1024d:N1} MB" + (usage.Incomplete ? " · 일부 파일 제외" : "");
        }
        _ = ReadStorage();
        content.Children.Add(Card(Column(Text("데이터", 17, true), Row(Button("방송 기록", () => Select(2)), Button("JSON 내보내기", () =>
        {
            var picker = new SaveFileDialog { Filter = "Windows 백업|*.json", FileName = "jjogae-windows-backup.json" };
            if (picker.ShowDialog(this) == true) StateStore.Export(app.State, picker.FileName);
        }), Button("JSON 가져오기", () =>
        {
            var picker = new OpenFileDialog { Filter = "Windows 백업|*.json" };
            if (picker.ShowDialog(this) == true && MessageBox.Show("현재 Windows 기록을 선택한 백업으로 바꿀까요?", "백업 가져오기", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { app.Import(picker.FileName); Render(); }
        })), SettingRow(storageTotal, Button("다시 확인", () => _ = ReadStorage())), storageDetails)));
        content.Children.Add(Card(Column(Text("YouTube 멤버십 표시", 17, true),
            PreferenceToggle("홈에서 멤버십 숨기기", settings.YouTubeMembershipHidden, value => settings.YouTubeMembershipHidden = value),
            PreferenceToggle("멤버십 직접 입력", settings.YouTubeMemberActive, value => settings.YouTubeMemberActive = value),
            Text("웹에서 시작일을 확인하지 못했을 때 직접 입력합니다.", 11, secondary: true),
            MembershipDateInput(),
            SettingRow(Text("이전 가입 개월"), MembershipMonthsInput()))));
        var browsers = new Dictionary<string, string> { ["system"] = "기본 브라우저", ["edge"] = "Microsoft Edge", ["chrome"] = "Google Chrome", ["firefox"] = "Firefox" };
        content.Children.Add(Card(Column(Text("외부 브라우저", 17, true), Text("채널·영상·카페 링크를 열 브라우저", 12, secondary: true),
            Choice(browsers.Values, browsers[settings.ExternalBrowser], value => { settings.ExternalBrowser = browsers.First(x => x.Value == value).Key; app.Save(); }))));
        content.Children.Add(Card(Column(Text("방송 보관", 17, true),
            PreferenceToggle("썸네일 자동 보관", settings.SaveThumbnails, value => settings.SaveThumbnails = value),
            Row(Button("방송 기록", () => Select(2)), Button("썸네일 관리", ShowThumbnailManager)),
            Text("방송 기록은 JSON 백업으로, 썸네일은 썸네일 관리에서 따로 내보냅니다.", 11, secondary: true))));
        content.Children.Add(Card(Column(Text("데스크톱 위젯", 17, true), Text("채널 요약을 별도 작은 창에 표시합니다.", 12, secondary: true),
            PreferenceToggle("팔로우·구독 기간 표시", settings.RelationshipDurationsVisible, value => settings.RelationshipDurationsVisible = value),
            Button("위젯 열기", ShowWidget))));
        InstallationSettings();
        content.Children.Add(Card(Column(Text("앱 안내", 17, true),
            Row(Button("사용 안내", () => AppController.OpenUrl("https://github.com/" + AppUpdates.Repository + "#readme")),
                Button("개인정보처리방침", () => AppController.OpenUrl("https://github.com/" + AppUpdates.Repository + "/blob/main/PRIVACY.md"))))));
        var cards = content.Children.Cast<UIElement>().Skip(firstCard).ToArray();
        foreach (var item in cards) content.Children.Remove(item);
        var masonry = new MasonryPanel(); foreach (var item in cards) masonry.Children.Add(item); content.Children.Add(masonry);
    }
    private CheckBox PreferenceToggle(string title, bool value, Action<bool> changed)
    {
        var check = new CheckBox { Content = title, IsChecked = value, Foreground = ink, Margin = new Thickness(0, 8, 0, 8) };
        check.Click += (_, _) => { changed(check.IsChecked == true); app.Save(); }; return check;
    }
    private DatePicker MembershipDateInput()
    {
        var picker = DateInput(app.State.Settings.YouTubeMemberSince);
        System.Windows.Automation.AutomationProperties.SetName(picker, "멤버십 시작일");
        picker.SelectedDateChanged += (_, _) => { app.State.Settings.YouTubeMemberSince = DateValue(picker); app.Save(); }; return picker;
    }
    private TextBox MembershipMonthsInput()
    {
        var input = new TextBox { Text = app.State.Settings.YouTubeAdditionalMonths.ToString(), Width = 90, MinHeight = 34 };
        System.Windows.Automation.AutomationProperties.SetName(input, "이전 가입 개월");
        input.LostKeyboardFocus += (_, _) => { if (int.TryParse(input.Text, out var value) && value is >= 0 and <= 1200) { app.State.Settings.YouTubeAdditionalMonths = value; app.Save(); } input.Text = app.State.Settings.YouTubeAdditionalMonths.ToString(); }; return input;
    }
    private void UpdateSettingsStatus()
    {
        var observation = app.State.YouTubeWeb;
        var youtubeLoggedIn = app.State.Settings.YouTubeWebEnabled && observation is not null;
        if (youtubeConnect is not null) { youtubeConnect.Visibility = youtubeLoggedIn ? Visibility.Collapsed : Visibility.Visible; youtubeConnect.IsEnabled = !app.YouTubeDisconnecting; }
        if (youtubeDisconnect is not null) youtubeDisconnect.Visibility = youtubeLoggedIn ? Visibility.Visible : Visibility.Collapsed;
        if (youtubeRelationship is not null) youtubeRelationship.Text = observation?.Subscribed switch { true => "구독 중", false => "구독하지 않음", _ => "미확인" };
        if (youtubeMembership is not null) youtubeMembership.Text = observation?.MembershipActive switch { true => observation.MembershipMonths is { } months ? $"{months}개월" : "이용 중", false => "이용하지 않음", _ => "미확인" };
        if (youtubeTier is not null) youtubeTier.Text = observation?.Tier ?? "—";
        if (youtubeBilling is not null) youtubeBilling.Text = observation?.NextBillingLabel ?? "—";
        if (youtubeChecked is not null) youtubeChecked.Text = observation is null ? "로그인 후 루파 멤버십 페이지에서 확인합니다." : $"{Channel.DateText(observation.CheckedAt)} 확인";
        var loggedIn = app.State.Account is not null;
        if (accountLabel is not null) accountLabel.Text = app.State.Account?.Nickname ?? "로그인하지 않음";
        if (settingsLoginButton is not null) { settingsLoginButton.Content = loggedIn ? "로그인 브라우저 열기" : "로그인"; settingsLoginButton.Visibility = Visibility.Visible; }
        if (settingsLogoutButton is not null) settingsLogoutButton.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        if (youtubeSubscribersLabel is not null)
        {
            youtubeSubscribersLabel.Text = app.State.YouTubeSubscribers is { } count ? $"{count:N0}명" : "—";
            youtubeSubscribersLabel.ToolTip = app.State.YouTubeCheckedAt is { } date ? $"공개 채널 · {Channel.DateText(date)} 확인" : "공개 채널에서 자동 확인";
        }
    }
    private UIElement SettingRow(UIElement label, UIElement control)
    {
        var grid = new Grid { MinHeight = 50, Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(label); Grid.SetColumn(control, 1); grid.Children.Add(control);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.SizeChanged += (_, _) =>
        {
            var stacked = grid.ActualWidth < 580;
            grid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : GridLength.Auto;
            Grid.SetRow(control, stacked ? 1 : 0); Grid.SetColumn(control, stacked ? 0 : 1);
            if (control is FrameworkElement element) element.HorizontalAlignment = stacked ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        };
        return new Border { Child = grid, BorderBrush = line, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 6) };
    }
}
