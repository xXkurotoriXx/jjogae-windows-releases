using System.Globalization;
using Microsoft.Win32;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private Window Dialog(string title, double width, double height) => new()
    {
        Title = title, Owner = this, Width = width, Height = height, MinWidth = 400, MinHeight = 340,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush(dark ? "#202536" : "#F3F4F9"), Foreground = ink,
        FontFamily = FontFamily, FontSize = 13
    };
    private void EditBroadcast(Broadcast? record)
    {
        var dialog = Dialog(record is null ? "과거 방송 추가" : "방송 시작·종료 시각 수정", 470, 430);
        var startValue = record?.EffectiveStartedAt ?? new DateTimeOffset((selectedDay ?? Channel.Today.AddDays(-1)).ToDateTime(new TimeOnly(18, 0)), TimeSpan.FromHours(9));
        var endValue = record?.EffectiveEndedAt ?? startValue.AddSeconds(record?.EffectiveSeconds > 0 ? record.EffectiveSeconds : 3600);
        TextBox Input(string value, string label)
        {
            var input = new TextBox { Text = value, MinHeight = 34, Padding = new Thickness(8), Margin = new Thickness(0, 3, 0, 8) };
            System.Windows.Automation.AutomationProperties.SetName(input, label); return input;
        }
        var title = Input(record?.Title ?? "", "방송 제목"); title.MaxLength = 300; title.IsReadOnly = record is not null;
        var start = Input(TimeZoneInfo.ConvertTime(startValue, Channel.Korea).ToString("yyyy-MM-dd HH:mm:ss"), "방송 시작 시각");
        var end = Input(TimeZoneInfo.ConvertTime(endValue, Channel.Korea).ToString("yyyy-MM-dd HH:mm:ss"), "방송 종료 시각");
        var error = Text("", 12); error.Foreground = Brushes.IndianRed;
        var save = Button("저장", () =>
        {
            try
            {
                DateTimeOffset Parse(string value)
                {
                    if (!DateTime.TryParseExact(value.Trim(), new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        throw new InvalidDataException("시각을 yyyy-MM-dd HH:mm 형식으로 입력해 주세요.");
                    return new DateTimeOffset(date, TimeSpan.FromHours(9));
                }
                var from = Parse(start.Text); var to = Parse(end.Text);
                if (record is null) app.AddBroadcast(title.Text, from, to); else app.UpdateBroadcastTiming(record.Id, from, to);
                month = RecordCalendar.Month(RecordCalendar.Day(from)); selectedDay = RecordCalendar.Day(from); broadcastUndated = false; broadcastPage = 0;
                dialog.Close(); Render();
            }
            catch (Exception e) { error.Text = e.Message; }
        });
        dialog.Content = new Border { Padding = new Thickness(20), Child = Column(Text("방송 제목"), title, Text("시작 · 한국 시간"), start,
            Text("종료 · 한국 시간"), end, Text("서버 원본 시각을 보존하며, 달력과 합계에 수정한 시간을 반영합니다.", 11, secondary: true), error, Row(save, Button("취소", () => dialog.Close()))) };
        dialog.ShowDialog();
    }
    private void ShowThumbnailManager()
    {
        var dialog = Dialog("방송 썸네일 관리", 780, 640);
        var selected = new HashSet<string>(); var list = new StackPanel(); var query = new TextBox { MinHeight = 34, Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 10) };
        System.Windows.Automation.AutomationProperties.SetName(query, "방송 제목 또는 날짜 검색");
        var message = Text("방송을 선택해 내보내거나, 저장한 폴더를 불러올 수 있습니다.", 12, secondary: true);
        Broadcast[] Visible() => app.State.Broadcasts.Where(x => string.IsNullOrWhiteSpace(query.Text) || x.Title.Contains(query.Text, StringComparison.OrdinalIgnoreCase) || Channel.DateText(x.Date).Contains(query.Text, StringComparison.Ordinal)).OrderByDescending(x => x.Date).ToArray();
        void Refresh()
        {
            list.Children.Clear();
            foreach (var record in Visible())
            {
                var check = new CheckBox { IsChecked = selected.Contains(record.Id), Content = Text(record.Title), Foreground = ink, Margin = new Thickness(0, 6, 0, 6) };
                check.Checked += (_, _) => selected.Add(record.Id); check.Unchecked += (_, _) => selected.Remove(record.Id);
                var saved = app.Thumbnails.PathFor(record) is not null;
                list.Children.Add(Card(Column(check, Text(Channel.DateText(record.Date) + (saved ? " · 보관됨" : " · 이미지 없음"), 11, secondary: true),
                    Row(Button("이미지 지정", async () =>
                    {
                        var picker = new OpenFileDialog { Title = "방송 썸네일 이미지 지정", Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp" };
                        if (picker.ShowDialog(dialog) != true) return;
                        await app.SetThumbnail(record.Id, picker.FileName); Refresh();
                    })))));
            }
        }
        query.TextChanged += (_, _) => Refresh();
        var buttons = Row(Button("전체 선택", () => { selected.UnionWith(Visible().Select(x => x.Id)); Refresh(); }), Button("선택 해제", () => { selected.Clear(); Refresh(); }),
            Button("선택 내보내기", () =>
            {
                var records = app.State.Broadcasts.Where(x => selected.Contains(x.Id) && app.Thumbnails.PathFor(x) is not null).ToArray();
                if (records.Length == 0) { message.Text = "보관된 썸네일이 있는 방송을 선택해 주세요."; return; }
                var picker = new OpenFolderDialog { Title = "선택한 썸네일 저장 위치" };
                if (picker.ShowDialog(dialog) == true) message.Text = "저장 완료 · " + app.Thumbnails.Export(records, picker.FolderName);
            }), Button("저장한 폴더 불러오기", async () =>
            {
                var picker = new OpenFolderDialog { Title = "thumbnails.json이 있는 폴더 선택" };
                if (picker.ShowDialog(dialog) != true) return;
                var count = await app.Thumbnails.ImportBundle(picker.FolderName, app.State); app.Save(); message.Text = $"{count}개 썸네일을 불러왔습니다."; Refresh();
            }));
        var dock = new DockPanel { Margin = new Thickness(20) };
        var top = Column(Text("방송 썸네일", 22, true), query, buttons, message); DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); dialog.Content = dock; Refresh(); dialog.ShowDialog(); Render();
    }
}
