using System.Windows.Controls.Primitives;
using System.Windows.Shapes;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private readonly RecordSelection cheeseSelection = new(Channel.Today);
    private bool allCheese;
    private Grid? recordPanels;
    private Border? recordPanelLeft, recordPanelRight;

    private Border MonthCalendar(DateOnly monthDate, DateOnly? selected, DateOnly? earliest,
        Func<DateOnly, (string Value, string Detail, Brush? Fill)> summary, Action<DateOnly> selectDay, Action<DateOnly> selectMonth, Action today,
        double cellHeight = 43)
    {
        monthDate = RecordCalendar.Month(monthDate);
        var previous = IconButton("chevron-left", "이전 달", () => selectMonth(monthDate.AddMonths(-1)));
        previous.Width = 30; previous.Height = 30; previous.Padding = new Thickness(6); previous.Margin = new Thickness(0);
        previous.IsEnabled = monthDate > RecordCalendar.Month(earliest ?? Channel.Today);
        var next = IconButton("chevron-right", "다음 달", () => selectMonth(monthDate.AddMonths(1)));
        next.Width = 30; next.Height = 30; next.Padding = new Thickness(6); next.Margin = new Thickness(0); next.IsEnabled = monthDate < RecordCalendar.Month(Channel.Today);
        var title = Text(monthDate.ToString("yyyy년 M월"), 17, true); title.HorizontalAlignment = HorizontalAlignment.Center;
        var top = new Grid(); foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) top.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        top.Children.Add(previous); Grid.SetColumn(title, 1); top.Children.Add(title); Grid.SetColumn(next, 2); top.Children.Add(next);
        var todayButton = Button("오늘", today); todayButton.MinHeight = 30; todayButton.Padding = new Thickness(10, 3, 10, 3); todayButton.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(todayButton, 3); top.Children.Add(todayButton);
        var cells = new UniformGrid { Columns = 7, Rows = 7, Tag = "record-calendar" };
        foreach (var dayName in new[] { "일", "월", "화", "수", "목", "금", "토" })
        { var label = Text(dayName, 11, secondary: true); label.HorizontalAlignment = HorizontalAlignment.Center; cells.Children.Add(label); }
        foreach (var day in RecordCalendar.Cells(monthDate))
        {
            if (day is not { } date) { cells.Children.Add(new Border { Height = cellHeight }); continue; }
            var (value, detail, fill) = summary(date);
            var label = Text(date.Day.ToString(), 12, selected == date); label.TextAlignment = TextAlignment.Center; label.Margin = new Thickness(0);
            if (date == Channel.Today) label.Foreground = accent;
            var contentLabel = Text(value, 10, true); contentLabel.Margin = new Thickness(0, 1, 0, 0); contentLabel.TextWrapping = TextWrapping.NoWrap; contentLabel.TextAlignment = TextAlignment.Center;
            var cell = Button("", () => selectDay(date)); cell.Content = Column(label, contentLabel); cell.Margin = new Thickness(2); cell.Padding = new Thickness(2); cell.Height = cellHeight; cell.MinHeight = 0;
            cell.HorizontalContentAlignment = HorizontalAlignment.Stretch; cell.Background = fill ?? Brushes.Transparent;
            cell.BorderThickness = new Thickness(selected == date ? 2 : date == Channel.Today ? 1 : 0); cell.BorderBrush = selected == date ? accent : line;
            cell.IsEnabled = date <= Channel.Today; cell.ToolTip = $"{date:yyyy.MM.dd} · {detail}"; cell.Tag = date;
            System.Windows.Automation.AutomationProperties.SetName(cell, $"{date:yyyy년 M월 d일} · {detail}");
            cells.Children.Add(cell);
        }
        var panel = Card(Column(top, cells)); panel.Tag = "month-calendar-panel"; panel.Padding = new Thickness(16, 12, 16, 12); return panel;
    }

    private void CheesePage()
    {
        var index = app.CheeseIndex; var selected = cheeseSelection.SelectedDay; var selectedMonth = cheeseSelection.SelectedMonth;
        var sync = Button("과거 전체 동기화", async () => { if (app.State.Account is null) app.ShowLogin(this); else await app.Refresh(true); }); sync.IsEnabled = !app.Busy;
        content.Children.Add(Row(sync, Button(allCheese ? "선택한 날짜" : "전체 기록", () => { allCheese = !allCheese; cheesePage = 0; Render(); })));
        var summary = new UniformGrid { Columns = 5 };
        foreach (var item in new[] { ("전체 사용", index.Total), ("최근 30일", index.LastDays(30, Channel.Today)), ("최근 7일", index.LastDays(7, Channel.Today)),
            ("일평균", index.MonthlyAverage(selectedMonth, Channel.Today)), ("오늘", index.Summary(Channel.Today).Total) })
        {
            var tile = Card(Column(Text(item.Item1, 11, secondary: true), FitNumber(item.Item2.ToString("N0"), 20)));
            tile.Padding = new Thickness(14, 10, 14, 10); tile.Margin = new Thickness(0, 0, 10, 14); summary.Children.Add(tile);
            if (item.Item1 == "일평균") tile.ToolTip = $"{selectedMonth:yyyy년 M월}의 경과 일수 기준";
        }
        content.Children.Add(summary);
        var maximum = index.Month(selectedMonth).Max(x => x.Total);
        var calendar = MonthCalendar(selectedMonth, selected, index.Earliest, day =>
        {
            var value = index.Summary(day); var medal = CheeseCalendarIndex.Medal(value.Total, maximum);
            return (value.Total > 0 ? RecordCalendar.Compact(value.Total) : "", $"{value.Count:N0}건 · {value.Total:N0} 치즈", MedalFill(medal));
        }, day => { cheeseSelection.Select(day); allCheese = false; cheesePage = 0; Render(); },
        next => { cheeseSelection.Select(RecordCalendar.SelectMonth(next, selected, Channel.Today)); allCheese = false; cheesePage = 0; Render(); },
        () => { cheeseSelection.Today(Channel.Today); allCheese = false; cheesePage = 0; Render(); });
        var legend = new WrapPanel { Margin = new Thickness(2, 4, 0, 0) };
        foreach (var (name, medal) in new[] { ("금", CheeseMedal.Gold), ("은", CheeseMedal.Silver), ("동", CheeseMedal.Bronze) })
        {
            var label = Text(name, 10, true); label.Margin = new Thickness(0);
            legend.Children.Add(new Border { Child = label, Background = MedalFill(medal), CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 2), Margin = new Thickness(0, 0, 6, 0) });
        }
        legend.ToolTip = "이달 하루 최대 사용량 기준: 금 2/3 이상 · 은 1/3 이상 · 동 1/3 미만";
        ((StackPanel)calendar.Child).Children.Add(legend);
        var chart = CheeseChart(index, selected);
        content.Children.Add(PairedPanels(calendar, chart));
        var records = allCheese ? index.All : index.Records(selected);
        var size = app.State.Settings.PageSize; cheesePage = Math.Clamp(cheesePage, 0, Math.Max(0, (records.Length - 1) / size));
        content.Children.Add(Text(allCheese ? "전체 기록" : $"{selected:yyyy년 M월 d일}", 17, true));
        content.Children.Add(PageControls(records.Length, size, cheesePage, page => { cheesePage = page; Render(); }, true));
        if (records.Length == 0) { content.Children.Add(Card(Text(app.State.Account is null && index.All.Length == 0 ? "로그인하면 사용 치즈 내역을 확인할 수 있습니다." : "이날 사용한 치즈가 없습니다.", secondary: true))); return; }
        var rows = new StackPanel();
        foreach (var record in Policies.Page(records, cheesePage, size))
        {
            var body = new Grid { Margin = new Thickness(0, 10, 0, 10) }; body.ColumnDefinitions.Add(new ColumnDefinition()); body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var message = Text(record.Message, 12, secondary: true); message.MaxHeight = 40; message.TextTrimming = TextTrimming.CharacterEllipsis; message.ToolTip = record.Message;
            body.Children.Add(Column(Text(record.Streamer, 14, true), message));
            var amount = Text($"{record.Amount:N0} 치즈", 16, true); amount.TextAlignment = TextAlignment.Right;
            var date = Text(Channel.DateText(record.At), 11, secondary: true); date.TextAlignment = TextAlignment.Right;
            var details = Column(amount, date); details.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(details, 1); body.Children.Add(details);
            rows.Children.Add(new Border { Child = body, BorderBrush = line, BorderThickness = new Thickness(0, 0, 0, 1), Tag = "cheese-record" });
        }
        content.Children.Add(Card(rows));
    }

    private UIElement FitNumber(string value, double size)
    {
        var label = Text(value, size, true); label.TextWrapping = TextWrapping.NoWrap; label.Margin = new Thickness(0);
        return new Viewbox { Child = label, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Height = size + 6, HorizontalAlignment = HorizontalAlignment.Left };
    }
    private Brush? MedalFill(CheeseMedal medal) => medal switch
    {
        CheeseMedal.Gold => Brush(dark ? "#685124" : "#F3DC9D"), CheeseMedal.Silver => Brush(dark ? "#424A5B" : "#DAE0EA"),
        CheeseMedal.Bronze => Brush(dark ? "#594132" : "#E9C8B1"), _ => null
    };
    private Grid PairedPanels(Border left, Border right)
    {
        recordPanels = new Grid(); recordPanelLeft = left; recordPanelRight = right;
        recordPanels.ColumnDefinitions.Add(new ColumnDefinition()); recordPanels.ColumnDefinitions.Add(new ColumnDefinition());
        recordPanels.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); recordPanels.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.VerticalAlignment = right.VerticalAlignment = VerticalAlignment.Stretch;
        recordPanels.Children.Add(left); recordPanels.Children.Add(right); return recordPanels;
    }
    private Border CheeseChart(CheeseCalendarIndex index, DateOnly selected)
    {
        var points = index.Fortnight(selected); var total = RecordCalendar.Sum(points.Select(x => x.Total));
        var title = Text("14일 치즈 사용량", 17, true); var period = Text($"{points[0].Day:yyyy.MM.dd} – {selected:MM.dd}", 11, secondary: true);
        var amount = Text($"{total:N0} 치즈", 20, true); amount.Margin = new Thickness(0, 6, 0, 0);
        var canvas = new Canvas { Height = 220, Margin = new Thickness(0, 14, 0, 0), ClipToBounds = true, Tag = "cheese-chart" };
        void Draw()
        {
            canvas.Children.Clear(); if (canvas.ActualWidth <= 100) return;
            var highest = Math.Max(1d, points.Max(x => x.Total)); var magnitude = Math.Pow(10, Math.Floor(Math.Log10(highest)));
            var upper = Math.Ceiling(highest / magnitude / 2) * magnitude * 2;
            const double left = 48, top = 10, chartHeight = 166; var plotWidth = canvas.ActualWidth - left - 8;
            for (var tick = 0; tick < 3; tick++)
            {
                var y = top + chartHeight * tick / 2; var value = (long)Math.Min(long.MaxValue - 2048d, upper * (2 - tick) / 2);
                var label = Text(RecordCalendar.Compact(value), 10, secondary: true); label.Width = left - 8; label.TextAlignment = TextAlignment.Right; Canvas.SetTop(label, y - 8); canvas.Children.Add(label);
                canvas.Children.Add(new Line { X1 = left, X2 = canvas.ActualWidth, Y1 = y, Y2 = y, Stroke = line, StrokeThickness = 1 });
            }
            var step = plotWidth / 14;
            for (var i = 0; i < points.Length; i++)
            {
                var point = points[i]; var barHeight = Math.Max(point.Total > 0 ? 2 : 0, chartHeight * point.Total / upper);
                var bar = new Border { Width = Math.Max(4, step * .58), Height = barHeight, CornerRadius = new CornerRadius(3, 3, 0, 0),
                    Background = point.Day == selected ? accent : Brush(dark ? "#D8B968" : "#C8A14B"), ToolTip = $"{point.Day:yyyy.MM.dd} · {point.Total:N0} 치즈", Tag = "cheese-bar" };
                Canvas.SetLeft(bar, left + step * i + step * .21); Canvas.SetTop(bar, top + chartHeight - barHeight); canvas.Children.Add(bar);
                if (i is 0 or 3 or 6 or 9 or 13)
                {
                    var date = Text(point.Day.ToString("M/d"), 10, secondary: true); date.Width = step * 1.6; date.TextAlignment = TextAlignment.Center;
                    Canvas.SetLeft(date, left + step * i - step * .3); Canvas.SetTop(date, top + chartHeight + 7); canvas.Children.Add(date);
                }
            }
        }
        canvas.SizeChanged += (_, _) => Draw();
        return Card(Column(title, period, amount, Text($"선택한 날 {index.Summary(selected).Total:N0} 치즈", 11, secondary: true), canvas));
    }
    private UIElement PageControls(int count, int size, int page, Action<int> changed, bool chooseSize = false)
    {
        var pages = Math.Max(1, (count + size - 1) / size); page = Math.Clamp(page, 0, pages - 1);
        var previous = Button("이전", () => changed(page - 1)); previous.IsEnabled = page > 0;
        var next = Button("다음", () => changed(page + 1)); next.IsEnabled = page + 1 < pages;
        var label = Text($"{page + 1} / {pages}페이지 · {count:N0}건", 12, secondary: true); label.Margin = new Thickness(0, 0, 12, 0);
        var controls = Row(previous, label, next);
        if (chooseSize) controls.Children.Insert(0, Choice(new[] { "10개씩", "20개씩", "50개씩", "100개씩" }, size + "개씩", value => { app.State.Settings.PageSize = int.Parse(value.Replace("개씩", "")); cheesePage = 0; app.Save(); }));
        return controls;
    }
}
