using System.Text.Json;
using Jjogae.Core;

static class MacParityTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.FromHours(9));
        var start = now.AddDays(-2);
        var first = new Broadcast { Id = "vod:1", VideoId = "1", LiveSessionId = "id-10", StartedAt = start, Seconds = 3600, VodSeconds = 3600 };
        var second = first with { Id = "vod:2", VideoId = "2", LiveSessionId = "10" };
        var unrelated = first with { Id = "vod:3", VideoId = "3", LiveSessionId = "11", StartedAt = start.AddDays(-1) };
        var edited = BroadcastTiming.Update([first, second, unrelated], first.Id, start.AddHours(-1), start.AddHours(2), now);
        check(edited.Single(x => x.Id == first.Id).StartedAt == start, "manual timing preserves service start");
        check(edited.Count(x => x.ManualTiming is not null) == 2, "manual timing propagates across normalized session IDs only");
        check(BroadcastLogic.DailySeconds(edited, now)[RecordCalendar.Day(start)] == 10800, "split VOD manual interval counted once");
        var merged = BroadcastLogic.Merge(edited, first with { Seconds = 7200, VodSeconds = 7200 });
        check(merged.Single(x => x.Id == first.Id).EffectiveSeconds == 10800, "remote refresh retains manual duration");
        check(merged.Single(x => x.Id == first.Id).VodSeconds == 7200, "remote duration remains available after edit");
        var unknown = first with { Id = "unknown", StartedAt = null, TimingSource = "observedEnd", Seconds = 0 };
        var recovered = BroadcastTiming.Update([unknown], unknown.Id, start, start.AddHours(1), now);
        check(BroadcastLogic.DailySeconds(recovered, now).Values.Single() == 3600, "manual edit restores undated observed record");
        void Reject(Action action, string name) { try { action(); check(false, name); } catch (InvalidDataException) { check(true, name); } }
        Reject(() => BroadcastTiming.Add("", start, start.AddHours(1), [], now), "blank manual title rejected");
        Reject(() => BroadcastTiming.Add("방송", start, start, [], now), "zero duration rejected");
        Reject(() => BroadcastTiming.Add("방송", now, now.AddMinutes(1), [], now), "future time rejected");
        Reject(() => BroadcastTiming.Add("방송", now.AddDays(-33), now, [], now), "overlong duration rejected");
        Reject(() => BroadcastTiming.Add("방송", start.AddMinutes(30), start.AddHours(2), [first], now), "overlapping manual broadcast rejected");
        check(BroadcastTiming.Add("별도 방송", start.AddHours(1), start.AddHours(2), [first], now).ManualTiming is not null, "adjacent broadcasts allowed");
        Reject(() => BroadcastTiming.Update([first], "missing", start, start.AddHours(1), now), "missing edit target rejected");
        var roundtrip = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(new AppState { Broadcasts = edited }, StateStore.Json), StateStore.Json)!;
        roundtrip.Normalize(); check(roundtrip.Broadcasts.Count(x => x.ManualTiming is not null) == 2, "manual timing survives save and restore");
        var custom = first with { ThumbnailFilename = "saved.png", ThumbnailIsCustom = true };
        check(BroadcastLogic.Merge([custom], first with { ThumbnailFilename = "remote.png" }).Single().ThumbnailFilename == "saved.png", "remote merge preserves custom artwork");
        foreach (var width in new[] { 430d, 600, 719 }) check(WindowLayout.Compact(width), "compact navigation below 720");
        check(!WindowLayout.Compact(720) && !WindowLayout.Compact(double.NaN), "desktop boundary and invalid geometry");
        check(WindowLayout.Columns(739) == 1 && WindowLayout.Columns(740) == 2 && WindowLayout.Columns(1120) == 3, "macOS card column thresholds");
        foreach (var count in new[] { 6, 7, 8 }) foreach (var columns in new[] { 1, 2, 3, 4 })
        { var rows = WindowLayout.BalancedRows(count, columns); check(rows.Sum() == count && rows.Max() - rows.Min() <= 1 && rows.Max() <= columns, "balanced overview tiles"); }
        var state = new AppState { Settings = new Preferences { YouTubeMemberActive = true, YouTubeMemberSince = Channel.Today.AddDays(-30) } };
        check(MembershipPresentation.Visible(state) && MembershipPresentation.Detail(state).Contains("직접 입력"), "manual membership is identified");
        check(MembershipPresentation.Cumulative(new(2026, 1, 31), new(2026, 2, 28), 3) == "4개월 1일", "membership respects calendar months at February boundary");
        check(MembershipPresentation.Cumulative(new(2026, 9, 26), new(2026, 9, 26), 2) == "2개월 1일", "membership includes its first day");
        state.Settings.YouTubeMembershipHidden = true; check(!MembershipPresentation.Visible(state), "hidden membership stays hidden");
        state.Settings.ExternalBrowser = "untrusted.exe"; state.Settings.YouTubeAdditionalMonths = -1; state.Settings.Normalize();
        check(state.Settings.ExternalBrowser == "system" && state.Settings.YouTubeAdditionalMonths == 0, "new settings normalized");
    }
}
