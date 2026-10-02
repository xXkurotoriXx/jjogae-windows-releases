namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private bool recoveryExpanded;
    private DateOnly recoveryStart = Channel.Today.AddDays(-6), recoveryEnd = Channel.Today;
    private readonly CafeHistoryScope recoveryScope = CafeHistoryScope.Notice;
    private string recoveryPreset = "최근 7일";
    private int recoveryPage;
    private readonly HashSet<string> selectedRecovery = [];

    private void ResetRecovery() { app.CancelRecovery(); selectedRecovery.Clear(); recoveryPage = 0; }
    private Border CafeRecoveryPanel()
    {
        var toggle = Button("", () =>
        {
            recoveryExpanded = !recoveryExpanded;
            if (!recoveryExpanded) ResetRecovery();
            Render();
        });
        toggle.Tag = "recovery-toggle"; toggle.Margin = new Thickness(0); toggle.BorderThickness = new Thickness(0); toggle.Background = Brushes.Transparent;
        toggle.HorizontalContentAlignment = HorizontalAlignment.Stretch; toggle.Padding = new Thickness(8, 4, 8, 4);
        System.Windows.Automation.AutomationProperties.SetName(toggle, "과거 글 불러오기 및 복구");
        toggle.ToolTip = recoveryExpanded ? "복구 영역 접기" : "복구 영역 펼치기";
        var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(Text("과거 글 불러오기 및 복구", 14, true));
        var chevron = IconView(recoveryExpanded ? "chevron-down" : "chevron-right", 16, muted); Grid.SetColumn(chevron, 1); heading.Children.Add(chevron); toggle.Content = heading;
        var body = Column(toggle);
        if (!recoveryExpanded) return Card(body);
        var panel = new StackPanel { Margin = new Thickness(8, 12, 8, 0) }; body.Children.Add(panel);
        var preset = Choice(new[] { "오늘", "최근 7일", "최근 30일", "직접 선택" }, recoveryPreset, value =>
        {
            recoveryPreset = value;
            if (value != "직접 선택") { recoveryEnd = Channel.Today; recoveryStart = recoveryEnd.AddDays(value == "최근 30일" ? -29 : value == "최근 7일" ? -6 : 0); }
            ResetRecovery(); Render();
        });
        var load = Button("조회", async () =>
        {
            selectedRecovery.Clear(); recoveryPage = 0;
            var pending = app.LoadCafeRecovery(new(recoveryStart, recoveryEnd, recoveryScope)); Render(); await pending; Render();
        }); load.IsEnabled = !app.RecoveryBusy;
        panel.Children.Add(Row(Text("공지"), preset, load));
        if (recoveryPreset == "직접 선택")
        {
            var from = DateInput(recoveryStart); var through = DateInput(recoveryEnd);
            from.SelectedDateChanged += (_, _) => { if (DateValue(from) is { } day) { recoveryStart = day; ResetRecovery(); } };
            through.SelectedDateChanged += (_, _) => { if (DateValue(through) is { } day) { recoveryEnd = day; ResetRecovery(); } };
            from.LostKeyboardFocus += (_, _) => Render(); through.LostKeyboardFocus += (_, _) => Render();
            panel.Children.Add(Row(from, Text("–"), through));
        }
        else panel.Children.Add(Text($"{recoveryStart:yyyy.MM.dd} – {recoveryEnd:yyyy.MM.dd}", 12, secondary: true));
        if (app.RecoveryBusy) panel.Children.Add(Row(Text("과거 글 조회 중", 13, secondary: true), Button("취소", () => { ResetRecovery(); Render(); })));
        if (app.Recovery is { } result && app.RecoveryCandidates is { } candidates)
        {
            selectedRecovery.IntersectWith(candidates.Articles.Select(x => x.Id));
            panel.Children.Add(Text($"불러올 글 {candidates.NewCount:N0}개 · 읽은 글 {candidates.SavedReadCount:N0}개 · 목록에 있는 글 {candidates.AlreadyVisible:N0}개", 12, secondary: true));
            if (result.Warning.Length > 0) panel.Children.Add(Text(result.Warning, 12, secondary: true));
            var restore = Button($"선택 복구 {selectedRecovery.Count:N0}개", async () => { var pending = app.RestoreCafe(selectedRecovery.ToArray()); Render(); await pending; selectedRecovery.Clear(); Render(); });
            restore.IsEnabled = selectedRecovery.Count > 0 && !app.RecoveryBusy;
            var selectAll = Button("결과 전체 선택", () => { selectedRecovery.UnionWith(candidates.Articles.Select(x => x.Id)); Render(); }); selectAll.IsEnabled = candidates.Articles.Length > 0;
            panel.Children.Add(Row(selectAll, Button("선택 해제", () => { selectedRecovery.Clear(); Render(); }), restore));
            recoveryPage = Math.Clamp(recoveryPage, 0, Math.Max(0, (candidates.Articles.Length - 1) / 20));
            panel.Children.Add(PageControls(candidates.Articles.Length, 20, recoveryPage, page => { recoveryPage = page; Render(); }));
            foreach (var post in Policies.Page(candidates.Articles, recoveryPage, 20))
            {
                var row = new Grid { Margin = new Thickness(0, 6, 0, 6) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
                var checkbox = new CheckBox { IsChecked = selectedRecovery.Contains(post.Id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                System.Windows.Automation.AutomationProperties.SetName(checkbox, post.Title + " 복구 선택");
                void UpdateSelection(bool isChecked)
                {
                    if (isChecked) selectedRecovery.Add(post.Id); else selectedRecovery.Remove(post.Id);
                    restore.Content = $"선택 복구 {selectedRecovery.Count:N0}개"; restore.IsEnabled = selectedRecovery.Count > 0 && !app.RecoveryBusy;
                }
                checkbox.Checked += (_, _) => UpdateSelection(true); checkbox.Unchecked += (_, _) => UpdateSelection(false); row.Children.Add(checkbox);
                var title = Text((post.Notice ? "공지 · " : post.Rupa ? "루파 · " : "") + post.Title, 13, true); title.MaxHeight = 40; title.TextTrimming = TextTrimming.CharacterEllipsis;
                if (post.Notice || post.Rupa) title.Foreground = Brush(post.Notice ? dark ? "#FFB84D" : "#8A4F00" : dark ? "#FA619E" : "#B52461");
                var detail = Column(title, Text($"{post.Author} · {post.Menu} · {Channel.DateText(post.At)}", 11, secondary: true)); Grid.SetColumn(detail, 1); row.Children.Add(detail); panel.Children.Add(row);
            }
            if (result.NextPage is not null)
            {
                var next = Button("이어서 조회", async () => { var pending = app.LoadCafeRecovery(result.Query, true); Render(); await pending; Render(); }); next.IsEnabled = !app.RecoveryBusy; panel.Children.Add(next);
            }
        }
        return Card(body);
    }
}
