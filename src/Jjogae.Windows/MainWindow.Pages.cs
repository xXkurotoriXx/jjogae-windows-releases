using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.Win32;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private Grid? overviewDashboard;
    private FrameworkElement? overviewHero, overviewMediaSection, overviewLinks;
    private bool OverviewExpanded => scroll.ActualWidth >= 1320 && scroll.ActualHeight >= 720;
    private void Overview()
    {
        var state = app.State;
        var live = state.Live;
        var chzzk = state.Media.Where(x => x.Source == "치지직" && x.Kind == "다시보기").OrderByDescending(x => x.PublishedAt).FirstOrDefault();
        var youtube = state.Media.FirstOrDefault(x => x.Source == "YouTube" && x.Id == state.YouTubeLatestVideoId)
            ?? state.Media.Where(x => x.Source == "YouTube").OrderByDescending(x => x.PublishedAt).FirstOrDefault();
        overviewDashboard = new Grid();
        overviewDashboard.ColumnDefinitions.Add(new ColumnDefinition()); overviewDashboard.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 4; i++) overviewDashboard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        overviewHero = MediaCard("치지직", live?.IsLive == true ? live.Title : chzzk?.Title ?? Channel.Name,
            live?.IsLive == true ? live.ImageUrl : chzzk?.ImageUrl ?? "",
            live?.IsLive == true && live.StartedAt is { } started ? "LIVE · " + Channel.Duration((DateTimeOffset.UtcNow - started).TotalSeconds) : chzzk is null ? "" : Channel.DateText(chzzk.PublishedAt),
            live?.IsLive == true ? Channel.Url : chzzk?.Url ?? Channel.Url, true);
        overviewDashboard.Children.Add(overviewHero);
        // Keep shared subdivisions fractional so rounding does not accumulate across a three-card row.
        var metrics = new Grid { UseLayoutRounding = false };
        overviewMetrics = metrics;
        var unread = state.Cafe.Where(x => x.Notice && !state.ReadCafe.Contains(x.Id)).ToArray();
        var tiles = new List<UIElement> {
            Metric("치지직 팔로워", state.Followers is { } followers ? $"{followers:N0}명" : "—", state.Account?.FollowedOn is { } followed ? $"팔로우 {Channel.Elapsed(followed)}\n{followed:yyyy.MM.dd}부터" : "", "chzzk"),
            Metric("YouTube 구독자", state.YouTubeSubscribers is { } subscribers ? $"{subscribers:N0}명" : "—", state.YouTubeWeb?.Subscribed switch { true => "구독 중", false => "구독하지 않음", _ => "" }, "youtube"),
            Metric("보유 통나무", state.Account?.Power?.ToString("N0") ?? "—", "", "chzzk"),
            Metric("카페 새 글", $"{unread.Length:N0}개", unread.Length == 0 ? "새 글을 모두 읽었습니다" : $"공지 {unread.Length}개", "naver-cafe") };
        if (state.Account is { IsSubscribed: true } account)
            tiles.Insert(3, Metric("치지직 구독", account.Subscription, account.Renewal, "chzzk", true));
        if (MembershipPresentation.Visible(state))
            tiles.Insert(2, Metric("YouTube 멤버십", MembershipPresentation.Value(state), MembershipPresentation.Detail(state), "youtube"));
        tiles.AddRange([
            Metric("루파 데뷔", Channel.Elapsed(Channel.Debut), "2025.06.15", "calendar"),
            Metric("루파 생일", $"D-{Channel.BirthdayRemaining(Channel.Today)}일", "7월 23일", "gift") ]);
        foreach (var item in tiles)
        { if (item is FrameworkElement element) element.Margin = new Thickness(0, 0, 12, 10); metrics.Children.Add(item); }
        overviewDashboard.Children.Add(metrics);
        homeMedia = new UniformGrid { Columns = 2, HorizontalAlignment = HorizontalAlignment.Stretch };
        homeMedia.Children.Add(MediaCard("YouTube", youtube?.Title ?? Channel.Name, youtube?.ImageUrl ?? "", youtube?.PublishedAt is null ? "" : Channel.DateText(youtube.PublishedAt), youtube?.Url ?? Channel.YouTubeUrl, false));
        var clip = state.Media.Where(x => x.Source == "치지직" && x.Kind == "클립").OrderByDescending(x => x.PublishedAt).FirstOrDefault();
        homeMedia.Children.Add(MediaCard("치지직 클립", clip?.Title ?? "표시할 클립이 없습니다.", clip?.ImageUrl ?? "", clip is null ? "" : Channel.DateText(clip.PublishedAt), clip?.Url ?? Channel.Url, true, true));
        overviewMediaSection = Column(Text("MEDIA", 10, true, true), Text("최신 미디어", 18, true), homeMedia);
        overviewDashboard.Children.Add(overviewMediaSection);
        var links = new UniformGrid { Columns = 2, Margin = new Thickness(-4, 5, -4, 10), Tag = "channel-shortcuts" };
        links.SizeChanged += (_, _) => links.Columns = links.ActualWidth >= 880 ? 4 : 2;
        foreach (var (title, icon, url) in new[] { ("치지직 채널", "chzzk", Channel.Url), ("루파 카페", "naver-cafe", Channel.CafeUrl),
            ("YouTube 아홀로 루파", "youtube", Channel.YouTubeUrl), ("YouTube 아홀로그", "youtube", Channel.YouTubeReplayUrl) })
        {
            var button = Button(title, () => app.Open(url));
            System.Windows.Automation.AutomationProperties.SetName(button, title);
            button.ToolTip = url;
            var label = new Grid();
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); label.ColumnDefinitions.Add(new ColumnDefinition());
            label.Children.Add(MetricIcon(icon)); var text = Text(title); text.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(text, 1); label.Children.Add(text);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Padding = new Thickness(10, 8, 10, 8); button.MinHeight = 60;
            button.Content = label; button.Tag = "shortcut:" + url; button.Margin = new Thickness(4); links.Children.Add(button);
        }
        overviewLinks = Column(Text("SHORTCUTS", 10, true, true), Text("바로가기", 18, true), links);
        overviewDashboard.Children.Add(overviewLinks); content.Children.Add(overviewDashboard);
    }

    private void UpdateOverviewLayout(double available)
    {
        if (section != 0 || overviewDashboard is null || overviewHero is null || overviewMetrics is null || overviewMediaSection is null || overviewLinks is null) return;
        var expanded = OverviewExpanded;
        overviewDashboard.ColumnDefinitions[0].Width = new GridLength(expanded ? 1.1 : 1, GridUnitType.Star);
        overviewDashboard.ColumnDefinitions[1].Width = expanded ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        foreach (var (element, row, column) in new[] { (overviewHero, 0, 0), (overviewMetrics, expanded ? 0 : 1, expanded ? 1 : 0),
            (overviewMediaSection, expanded ? 1 : 2, 0), (overviewLinks, expanded ? 1 : 3, expanded ? 1 : 0) })
        { Grid.SetRow(element, row); Grid.SetColumn(element, column); }
        if (homeMedia is not null) homeMedia.Columns = expanded ? 1 : WindowLayout.Columns(available) > 1 ? 2 : 1;
    }

    private Border MediaCard(string source, string title, string image, string date, string url, bool chzzk, bool clip = false)
    {
        var name = Text(source, 12, true, true); name.Foreground = chzzk ? Brush(dark ? "#77DFC0" : "#138060") : accent;
        name.Margin = new Thickness(0, 0, 0, 6);
        var headline = Text(title, 16, true); headline.Margin = new Thickness(0); headline.MaxHeight = 40; headline.LineHeight = 20; headline.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; headline.TextTrimming = TextTrimming.CharacterEllipsis; headline.ToolTip = title;
        var published = Text(date, 11, secondary: true); published.Margin = new Thickness(0, 6, 0, 6);
        var open = Button(clip ? "클립 열기" : "영상 열기", () => app.Open(url)); open.Margin = new Thickness(0); open.Foreground = accent;
        open.Width = 88; open.Height = 36; open.VerticalAlignment = VerticalAlignment.Center; open.HorizontalAlignment = HorizontalAlignment.Left;
        var info = Column(name, headline, published, open); info.Margin = new Thickness(16, 0, 0, 0); info.VerticalAlignment = VerticalAlignment.Center;
        var panel = new Grid(); panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var thumbnail = (FrameworkElement)Thumbnail(image); thumbnail.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(thumbnail); Grid.SetColumn(info, 1); panel.Children.Add(info);
        panel.SizeChanged += (_, _) =>
        {
            var vertical = panel.ActualWidth < 480 && source == "치지직" || panel.ActualWidth < 350;
            panel.ColumnDefinitions[1].Width = vertical ? new GridLength(0) : new GridLength(3, GridUnitType.Star);
            Grid.SetRow(info, vertical ? 1 : 0); Grid.SetColumn(info, vertical ? 0 : 1);
            info.Margin = vertical ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
        };
        var cardView = Card(panel); cardView.Tag = "home-media:" + source; cardView.Margin = new Thickness(0, 0, 12, 12); cardView.Padding = new Thickness(12); return cardView;
    }


    private Button? cafeReadSelected;
    private void UpdateCafeSelection() { if (cafeReadSelected is not null) cafeReadSelected.Visibility = selectedCafe.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }
    private void CafePage()
    {
        var posts = app.State.Cafe.Where(x => x.Notice && !app.State.ReadCafe.Contains(x.Id)).OrderByDescending(x => x.At).ToArray();
        selectedCafe.IntersectWith(posts.Select(x => x.Id));
        cafeReadSelected = Button("선택 읽음", () => { var ids = selectedCafe.ToArray(); selectedCafe.Clear(); UpdateCafeSelection(); app.ReadCafe(ids); });
        UpdateCafeSelection();
        content.Children.Add(Row(Button("새로고침", async () => await app.RefreshCafe()), cafeReadSelected,
            Button("모두 읽음", () => app.ReadCafe(posts.Select(x => x.Id))), Button("카페 열기", () => app.Open(Channel.CafeUrl))));
        content.Children.Add(CafeRecoveryPanel());
        if (posts.Length == 0) { content.Children.Add(Card(Text(app.State.Account is null ? "로그인하면 카페 새 글을 확인할 수 있어요." : "읽지 않은 새 글이 없습니다."))); return; }
        foreach (var notice in new[] { true, false })
        {
            var group = posts.Where(x => x.Notice == notice).ToArray(); if (group.Length == 0) continue;
            var heading = Text($"{(notice ? "새 공지" : "새 글")} {group.Length:N0}개", 16, true); heading.Margin = new Thickness(0, 6, 0, 10); content.Children.Add(heading);
            var rows = new StackPanel();
            foreach (var post in group.Take(200))
            {
                if (rows.Children.Count > 0) rows.Children.Add(new Border { Height = 1, Background = line, Margin = new Thickness(48, 0, 0, 0) });
                rows.Children.Add(CafeRow(post));
            }
            var container = Card(rows); container.Padding = new Thickness(0);
            container.SizeChanged += (_, _) => container.Clip = new RectangleGeometry(new Rect(container.RenderSize), 16, 16);
            content.Children.Add(container);
            if (group.Length > 200) content.Children.Add(Text("한 번에 200개까지 표시합니다. 읽음 처리하면 다음 글이 나타납니다.", 11, secondary: true));
        }
    }

    private Border CafeRow(CafePost post)
    {
        var (label, color) = post.Notice ? ("공지", dark ? "#FFB84D" : "#8A4F00")
            : post.Rupa ? ("루파 작성", dark ? "#FA619E" : "#B52461") : ("새 글", dark ? "#3D9CFA" : "#0069B8");
        var tint = Brush(color); var background = tint.Clone(); background.Opacity = post.Notice ? .075 : .045;
        var badgeBackground = tint.Clone(); badgeBackground.Opacity = .13;
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var selected = new CheckBox { IsChecked = selectedCafe.Contains(post.Id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        System.Windows.Automation.AutomationProperties.SetName(selected, post.Title + " 선택");
        selected.Checked += (_, _) => { selectedCafe.Add(post.Id); UpdateCafeSelection(); };
        selected.Unchecked += (_, _) => { selectedCafe.Remove(post.Id); UpdateCafeSelection(); }; row.Children.Add(selected);

        var headline = new Grid(); headline.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); headline.ColumnDefinitions.Add(new ColumnDefinition());
        var badgeText = Text(label, 11, true); badgeText.Foreground = tint; badgeText.Margin = new Thickness(0);
        var badge = new Border { Child = badgeText, Background = badgeBackground, CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center }; headline.Children.Add(badge);
        var title = Text(post.Title, 14, true); title.Margin = new Thickness(0); title.MaxHeight = 40; title.LineHeight = 20; title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; title.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(title, 1); headline.Children.Add(title);
        var details = Text("", 11, secondary: true); details.Margin = new Thickness(0, 6, 0, 0); details.TextWrapping = TextWrapping.NoWrap; details.TextTrimming = TextTrimming.CharacterEllipsis;
        details.Inlines.Add(new System.Windows.Documents.Run(post.Author) { Foreground = post.Rupa && !post.Notice ? tint : muted });
        details.Inlines.Add($" · {post.Menu} · {Channel.DateText(post.At)}");
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.Children.Add(Column(headline, details));
        var external = new Border { Child = IconView("external", 15, tint), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(external, 1); body.Children.Add(external);
        var open = Button("", () => app.OpenCafe(post)); open.Content = body; open.Background = Brushes.Transparent; open.BorderThickness = new Thickness(0); open.Padding = new Thickness(0); open.Margin = new Thickness(0); open.MinHeight = 0; open.HorizontalContentAlignment = HorizontalAlignment.Stretch; open.ToolTip = post.Title;
        System.Windows.Automation.AutomationProperties.SetName(open, label + ": " + post.Title);
        Grid.SetColumn(open, 1); row.Children.Add(open);
        return new Border { Child = row, Tag = "cafe-" + (post.Notice ? "notice" : post.Rupa ? "rupa" : "standard"), Background = background, BorderBrush = tint, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(14, 12, 16, 12), MinHeight = 76 };
    }

}
