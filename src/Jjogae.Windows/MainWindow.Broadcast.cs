using System.Windows.Controls.Primitives;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private bool broadcastUndated;
    private int broadcastPage;
    private void BroadcastPage()
    {
        var records = app.State.Broadcasts.Select(BroadcastLogic.Normalize).ToArray();
        var days = BroadcastLogic.DailySeconds(records); var summary = BroadcastSummary.Calculate(days, month, Channel.Today);
        var unknown = records.Count(x => x.EffectiveStartedAt is null);
        content.Children.Add(Row(Button("다시보기 동기화", async () => await app.LoadBroadcastHistory()),
            Button("과거 방송 추가", () => EditBroadcast(null)), Button("썸네일 관리", ShowThumbnailManager),
            Button(broadcastUndated ? "월별 방송" : $"시작일 미확인 {unknown:N0}개", () => { broadcastUndated = !broadcastUndated; broadcastPage = 0; Render(); })));
        var earliest = records.Where(x => x.EffectiveStartedAt is not null).Select(x => RecordCalendar.Day(x.EffectiveStartedAt!.Value)).DefaultIfEmpty(Channel.Today).Min();
        var calendar = MonthCalendar(month, selectedDay, earliest,
            day => (days.GetValueOrDefault(day) > 0 ? ShortDuration(days[day]) : "", Channel.Duration(days.GetValueOrDefault(day)), days.GetValueOrDefault(day) > 0 ? Brush(dark ? "#285D4D" : "#CDEBDE") : null),
            day => { selectedDay = selectedDay == day ? null : day; broadcastUndated = false; broadcastPage = 0; Render(); },
            next => { month = next; selectedDay = null; broadcastUndated = false; broadcastPage = 0; Render(); },
            () => { month = RecordCalendar.Month(Channel.Today); selectedDay = null; broadcastUndated = false; broadcastPage = 0; Render(); });
        var difference = summary.PreviousMonthDifference is { } delta ? (delta < 0 ? "−" : "+") + Channel.Duration(Math.Abs(delta)) : "—";
        var stats = Column(Text($"{month:yyyy년 M월} 방송", 17, true), Text(Channel.Duration(summary.Total), 27, true), Text($"방송 {summary.BroadcastDays:N0}일", 12, secondary: true),
            StatisticRow("방송일 평균", Channel.Duration(summary.BroadcastDayAverage)),
            StatisticRow("일평균", Channel.Duration(summary.CalendarDayAverage)),
            StatisticRow("전체 방송일 평균", Channel.Duration(summary.OverallBroadcastDayAverage)),
            StatisticRow("전월 방송일 평균 대비", difference),
            StatisticRow("월평균", Channel.Duration(summary.MonthlyAverage)));
        content.Children.Add(PairedPanels(calendar, Card(stats)));
        var filtered = records.Where(x => broadcastUndated ? x.EffectiveStartedAt is null : x.EffectiveStartedAt is { } start
            && (selectedDay is { } day ? RecordCalendar.Day(start) == day : RecordCalendar.Month(RecordCalendar.Day(start)) == month))
            .OrderByDescending(x => x.Date).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        content.Children.Add(Text(broadcastUndated ? "시작일 미확인" : selectedDay?.ToString("yyyy년 M월 d일 방송") ?? "이달 방송", 17, true));
        if (broadcastUndated && filtered.Length > 0) content.Children.Add(Text("방송시간 합계는 시작 시각을 확인한 뒤 반영됩니다.", 12, secondary: true));
        broadcastPage = Math.Clamp(broadcastPage, 0, Math.Max(0, (filtered.Length - 1) / 20));
        content.Children.Add(PageControls(filtered.Length, 20, broadcastPage, page => { broadcastPage = page; Render(); }));
        if (filtered.Length == 0) { content.Children.Add(Card(Text("해당하는 방송 기록이 없습니다.", secondary: true))); return; }
        var tiles = new UniformGrid { Columns = 2 }; archiveTiles = tiles;
        foreach (var record in Policies.Page(filtered, broadcastPage, 20))
        {
            var title = Text(record.Title, 15, true); title.Height = 42; title.TextTrimming = TextTrimming.CharacterEllipsis; title.ToolTip = record.Title;
            var duration = BroadcastLogic.ValidDuration(record.EffectiveSeconds) && (record.ManualTiming is not null || record.TimingSource != "observedEnd") ? Channel.Duration(record.EffectiveSeconds) : "시간 확인 중";
            var time = record.EffectiveStartedAt is null ? $"게시 {Channel.DateText(record.PublishedAt)}" : Channel.DateText(record.EffectiveStartedAt);
            var open = Button(record.VideoId is null ? "채널 열기" : "다시보기", () => app.Open(record.Url)); open.Width = 100; open.Height = 36;
            var local = app.Thumbnails.Load(record);
            UIElement artwork = local is null ? Thumbnail(record.ImageUrl) : new AspectFrame { Child = new Image { Source = local, Stretch = Stretch.UniformToFill } };
            var tile = Card(Column(artwork, new Border { Height = 8 }, title, Text(time, 11, secondary: true), Text(duration, 14, true),
                Text(record.ManualTiming is not null ? "직접 수정한 방송 시간" : record.TimingSource == "liveTiming" ? "방송 시작·종료 기준" : "다시보기 길이", 11, secondary: true),
                Row(open, Button("시간 수정", () => EditBroadcast(record)))));
            tile.Padding = new Thickness(12); tile.Margin = new Thickness(0, 0, 12, 12); tiles.Children.Add(tile);
        }
        content.Children.Add(tiles);
    }
    private UIElement StatisticRow(string label, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 9, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Text(label, 12, secondary: true)); var number = Text(value, 14, true); Grid.SetColumn(number, 1); grid.Children.Add(number); return grid;
    }
    private static string ShortDuration(double seconds)
    {
        var minutes = (long)Math.Max(0, seconds) / 60;
        return minutes >= 60 ? $"{minutes / 60}시간 {minutes % 60}분" : $"{minutes}분";
    }
}
