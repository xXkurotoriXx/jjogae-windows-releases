namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private Window? widget;
    private void ShowWidget()
    {
        if (widget is not null) { widget.Show(); widget.Activate(); return; }
        var window = Dialog("루파 상황 위젯", 400, 480); window.MinWidth = 320; window.MinHeight = 360; window.Owner = null; window.Topmost = true;
        widget = window;
        void Refresh()
        {
            var state = app.State;
            var body = Column(Text("아홀로 루파", 22, true), Text(state.Live?.IsLive == true ? "● LIVE · " + state.Live.Title : "방송 대기 중", 13, true),
                StatisticRow("치지직 팔로워", state.Followers?.ToString("N0") ?? "확인 중"), StatisticRow("YouTube 구독자", state.YouTubeSubscribers?.ToString("N0") ?? "확인 중"),
                StatisticRow("카페 새 공지", state.Cafe.Count(x => x.Notice && !state.ReadCafe.Contains(x.Id)).ToString("N0") + "개"));
            if (state.Settings.RelationshipDurationsVisible)
            {
                if (state.Account?.FollowedOn is { } date) body.Children.Add(StatisticRow("팔로우", Channel.Elapsed(date)));
                if (state.Account?.IsSubscribed == true) body.Children.Add(StatisticRow("치지직 구독", state.Account.Subscription));
                if (MembershipPresentation.Visible(state)) body.Children.Add(StatisticRow("YouTube 멤버십", MembershipPresentation.Value(state)));
            }
            body.Children.Add(Text("마지막 업데이트 · " + Channel.DateText(state.LastUpdatedAt), 11, secondary: true));
            body.Children.Add(Row(Button("상황실 열기", () => { Show(); WindowState = WindowState.Normal; Activate(); Select(0); }), Button("방송 기록", () => { Show(); WindowState = WindowState.Normal; Activate(); Select(2); })));
            var topmost = new CheckBox { Content = "항상 위에 표시", IsChecked = window.Topmost, Foreground = ink, Margin = new Thickness(0, 8, 0, 0) };
            topmost.Click += (_, _) => window.Topmost = topmost.IsChecked == true; body.Children.Add(topmost);
            window.Background = Brush(dark ? "#202536" : "#F3F4F9"); window.Content = new ScrollViewer { Content = new Border { Padding = new Thickness(20), Child = body }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        app.Changed += Refresh;
        window.Closed += (_, _) => { app.Changed -= Refresh; widget = null; };
        Closed += (_, _) => window.Close(); Refresh(); window.Show();
    }
}
