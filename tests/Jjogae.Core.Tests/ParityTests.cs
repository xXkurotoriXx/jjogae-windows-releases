using System.Net;
using System.Text;
using System.Text.Json;
using Jjogae.Core;

internal static class ParityTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.FromHours(9)); var today = new DateOnly(2026, 9, 18);
        for (var year = 2024; year <= 2028; year++) for (var month = 1; month <= 12; month++)
        {
            var date = new DateOnly(year, month, 1); var cells = RecordCalendar.Cells(date);
            check(cells.Length == 42 && cells.OfType<DateOnly>().Count() == DateTime.DaysInMonth(year, month), "calendar stable six weeks / " + date);
            check(cells[(int)date.DayOfWeek] == date, "Sunday-first calendar / " + date);
        }
        check(RecordCalendar.Day(new DateTimeOffset(2026, 8, 31, 16, 0, 0, TimeSpan.Zero)) == new DateOnly(2026, 9, 1), "Korean day across UTC date");
        check(RecordCalendar.SelectMonth(new(2024, 2, 1), new(2024, 1, 31), today) == new DateOnly(2024, 2, 29), "leap-month selection clamp");
        check(RecordCalendar.SelectMonth(new(2026, 9, 1), new(2026, 8, 31), today) == today, "current-month selection excludes future");
        check(RecordCalendar.ElapsedMonthDays(new(2026, 8, 1), today) == 31 && RecordCalendar.ElapsedMonthDays(today, today) == 18 && RecordCalendar.ElapsedMonthDays(today.AddMonths(1), today) == 0, "average calendar-day divisors");
        check(RecordCalendar.Sum([long.MaxValue, 2, -10]) == long.MaxValue, "cheese sum saturates without overflow");
        check(RecordCalendar.Compact(12345) == "1.2만" && RecordCalendar.Compact(100000000) == "1억", "compact Korean chart labels");
        var selection = new RecordSelection(today); selection.Advance(today.AddDays(1)); check(selection.SelectedDay == today.AddDays(1), "selection follows Korean midnight");
        selection.Select(today.AddDays(-4)); selection.Advance(today.AddDays(2)); check(selection.SelectedDay == today.AddDays(-4), "history selection survives midnight");
        selection.Today(today); check(selection.SelectedDay == today, "record selection resets to today");
        var rows = new[] { new Cheese("a", "테스트", 200, "", "", now), new Cheese("b", "테스트", 100, "", "", now.AddDays(-1)), new Cheese("c", "테스트", 50, "", "", now.AddDays(-40)), new Cheese("bad", "", -1, "", "", now) };
        var index = new CheeseCalendarIndex(rows.Concat([rows[0]]));
        check(index.All.Length == 3 && index.Total == 350, "cheese dedupe and nonnegative records");
        check(index.Fortnight(today).Length == 14 && index.Fortnight(today).Count(x => x.Total == 0) == 12, "chart has zero-filled fourteen days");
        check(index.Records(today).Length == 1 && index.LastDays(7, today) == 300 && index.LastDays(30, today) == 300, "day and recent cheese totals");
        check(index.MonthlyAverage(today, today) == 16 && index.MonthlyAverage(today.AddMonths(1), today) == 0, "month average includes zero days, excludes future");
        foreach (var maximum in new[] { 1L, 2, 3, 10, 300, long.MaxValue })
        {
            check(CheeseCalendarIndex.Medal(0, maximum) == CheeseMedal.None && CheeseCalendarIndex.Medal(maximum, maximum) == CheeseMedal.Gold, "medal extremes");
            check(CheeseCalendarIndex.Medal(maximum - maximum / 3, maximum) == CheeseMedal.Gold, "gold boundary without overflow");
        }
        check(CheeseCalendarIndex.Medal(99, 300) == CheeseMedal.Bronze && CheeseCalendarIndex.Medal(100, 300) == CheeseMedal.Silver && CheeseCalendarIndex.Medal(200, 300) == CheeseMedal.Gold, "medal thirds");

        var live = new Broadcast { Id = "live:1", LiveSessionId = "id-1", StartedAt = now.AddHours(-2), EndedAt = now, Seconds = 7200, TimingSource = "liveTiming", Title = "방송" };
        var replay = new Broadcast { Id = "vod:42", VideoId = "42", LiveSessionId = "id-1", PublishedAt = now.AddHours(2), Seconds = 6000, VodSeconds = 6000, Url = "https://chzzk.naver.com/video/42" };
        var merged = BroadcastLogic.Merge([live], replay);
        check(merged.Count == 1 && merged[0].Seconds == 7200 && merged[0].VodSeconds == 6000 && merged[0].VideoId == "42", "exact live timing keeps replay recovery metadata");
        check(BroadcastLogic.DailySeconds(merged, now)[today] == 7200, "merged live and replay counted once");
        var part = replay with { Id = "vod:43", VideoId = "43" };
        check(BroadcastLogic.Merge([replay], part).Count == 2 && BroadcastLogic.Merge([replay, part], live).Count == 2, "separate VOD parts retained");
        var shifted = live with { Id = "different", LiveSessionId = "id-2", StartedAt = live.StartedAt!.Value.AddSeconds(30) };
        check(BroadcastLogic.Merge([live], shifted).Count == 2, "distinct adjacent broadcasts not merged by two-minute heuristic");
        var overlap = live with { Id = "overlap", StartedAt = now.AddHours(-1), EndedAt = now.AddHours(1), Seconds = 7200 };
        check(BroadcastLogic.DailySeconds([live, overlap], now.AddHours(2))[today] == 10800, "overlap union prevents double count");
        check(BroadcastLogic.DailySeconds([live with { Seconds = double.NaN }, live with { Id = "pending", TimingSource = "observedEnd" }], now).Count == 0, "invalid and observed duration excluded");
        check(BroadcastLogic.Normalize(live with { TimingSource = "vod" }).StartedAt is null, "legacy inferred VOD date invalidated for recheck");
        check(BroadcastLogic.NeedsDetail(replay, now) && !BroadcastLogic.NeedsDetail(replay with { DetailCheckedAt = now }, now), "missing date retries bounded to fifteen minutes");
        using (var json = JsonDocument.Parse("""{"videoNo":42,"videoTitle":"공개 영상","videoType":"REPLAY","publishDate":"2026-09-18 16:00:00","duration":3600,"liveRewindPlaybackJson":"{\"meta\":{\"liveId\":123}}"}"""))
        {
            var parsed = ApiClient.ParseBroadcast(json.RootElement)!;
            check(parsed.StartedAt is null && parsed.PublishedAt is not null && parsed.Seconds == 3600 && parsed.LiveSessionId == "id-123", "publication not used as broadcast start");
        }
        var totals = new Dictionary<DateOnly, double> { [today] = 7200, [today.AddDays(-1)] = 3600, [today.AddMonths(-1)] = 3600 };
        var summary = BroadcastSummary.Calculate(totals, today, today);
        check(summary.Total == 10800 && summary.BroadcastDayAverage == 5400 && summary.CalendarDayAverage == 600, "broadcast monthly average bases");
        check(summary.OverallBroadcastDayAverage == 4800 && summary.PreviousMonthDifference == 1800 && summary.MonthlyAverage == 7200, "weighted overall and previous-month averages");
        var state = new AppState { Settings = new Preferences { Notifications = true } }; Policies.ObserveMedia(state, [], new(true, "1", "테스트 방송", now.AddHours(-2), null, ""), null, null, now.AddHours(-2));
        Policies.ObserveMedia(state, [], new(false, "1", "", null, null, ""), null, null, now.AddHours(10));
        check(state.Pending.Single(x => x.Kind == "방송 종료").TimeBasis == "observed", "offline end queued with observation time");
        Policies.MergeBroadcast(state, replay);
        var endEvent = state.Pending.Single(x => x.Kind == "방송 종료");
        check(endEvent.TimeBasis == "replay" && endEvent.At == now.AddHours(-2).AddSeconds(6000) && endEvent.Title.Contains("1시간 40분"), "replay repairs end time and duration without duplicate alert");
        check(state.Pending.Count(x => x.Kind == "방송 종료") == 1, "replay updates pending alert without duplication");
        state.Pending.Clear(); Policies.MergeBroadcast(state, replay);
        check(state.Pending.Count == 0, "replay recovery does not re-notify after delivery");

        await Cafe(check);
        await Profile(check, now);
        var serialized = JsonSerializer.Serialize(new AppState { Account = new("runtime", null, "", null, "", new string('a', 32)), ChatProfile = new(new string('a', 32), Channel.Id, [], 1, "", null, null, now) }, StateStore.Json);
        check(!serialized.Contains("runtime") && !serialized.Contains("chatProfile") && !serialized.Contains("userId"), "account/profile excluded from backup");
        check(JsonSerializer.Deserialize<Broadcast>(JsonSerializer.Serialize(replay, StateStore.Json), StateStore.Json)?.ThumbnailFilename is null, "legacy replay has no local thumbnail until downloaded");
    }

    private static async Task Cafe(Action<bool, string> check)
    {
        var day = Channel.Today; var at = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(9));
        CafePost Post(string id, bool notice = false, string author = "테스트") => new(id, "테스트 " + id, author, "게시판", notice, at, Channel.CafeUrl);
        var query = new CafeHistoryQuery(day.AddDays(-6), day); var saved = new AppState { Cafe = [Post("read"), Post("visible")], ReadCafe = ["read"] };
        var before = JsonSerializer.Serialize(saved, StateStore.Json);
        var candidates = new CafeRecoveryCandidates(saved, query, [Post("new"), Post("read"), Post("visible")]);
        check(candidates.NewCount == 1 && candidates.SavedReadCount == 1 && candidates.AlreadyVisible == 1 && candidates.Articles.Length == 2, "recovery candidates exclude already unread posts");
        check(JsonSerializer.Serialize(saved, StateStore.Json) == before, "query does not mutate saved records or read state");
        check(candidates.Restore(saved, ["read"]) == 1 && !saved.ReadCafe.Contains("read") && saved.Cafe.All(x => x.Id != "new"), "restore only selected articles");
        var scope = new CafeRecoveryCandidates(new(), query with { Scope = CafeHistoryScope.Rupa }, [Post("r", author: Channel.Name), Post("n", true), Post("o")]);
        check(scope.Articles.Single().Id == "r", "creator-only history filter");
        check(new CafeRecoveryCandidates(new(), query with { Scope = CafeHistoryScope.Notice }, [Post("r", author: Channel.Name), Post("n", true)]).Articles.Single().Id == "n", "notice-only history filter");
        var seenPages = new List<int>();
        var result = await CafeHistoryLoader.Fetch(query, 1, (page, _) => { seenPages.Add(page); return Task.FromResult(new CafeHistoryPage(Enumerable.Range(0, 20).Select(i => Post($"{page}-{i}")).ToArray())); }, delayMilliseconds: 0);
        check(result.PagesFetched == 50 && result.NextPage == 51 && result.Articles.Count == 1000, "history bounded to fifty pages with continuation");
        result = await CafeHistoryLoader.Fetch(query, 51, (page, _) => Task.FromResult(new CafeHistoryPage([Post("last")], 1001)), delayMilliseconds: 0);
        check(result.PagesFetched == 1 && result.NextPage is null, "continuation begins at next page and respects total count");
        result = await CafeHistoryLoader.Fetch(query, 1, (page, _) => page == 2 ? throw new HttpRequestException() : Task.FromResult(new CafeHistoryPage(Enumerable.Range(0, 20).Select(i => Post(i.ToString())).ToArray())), delayMilliseconds: 0);
        check(result.Articles.Count == 20 && result.NextPage == 2 && result.Warning.Length > 0, "mid-fetch failure retains partial preview and retry page");
        result = await CafeHistoryLoader.Fetch(query, 1, (_, _) => Task.FromResult(new CafeHistoryPage(Enumerable.Range(0, 20).Select(i => Post(i.ToString())).ToArray())), delayMilliseconds: 0);
        check(result.PagesFetched == 2 && result.Articles.Count == 20 && result.NextPage is null, "repeated page terminates without looping");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var cancelled = false;
        try { await CafeHistoryLoader.Fetch(query, 1, (_, _) => throw new Exception("must not run"), cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "cancelled history never fetches or restores");
    }

    private static async Task Profile(Action<bool, string> check, DateTimeOffset now)
    {
        var id = new string('a', 32);
        var fixture = """{"code":200,"content":{"userIdHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","streamingProperty":{"subscription":{"accumulativeMonth":12,"tierName":"테스트 티어","badge":{"imageUrl":"https://ssl.pstatic.net/sub.png"}},"donationActivity":{"continuousDonationDays":3},"following":{"followDate":"2026-01-02 12:30:00"}},"viewerBadges":[{"activatedV2":false,"badge":{"badgeId":"hidden","imageUrl":"https://ssl.pstatic.net/hidden.png"}},{"activatedV2":true,"order":1,"badge":{"badgeId":"global","scope":"GLOBAL","imageUrl":"https://ssl.pstatic.net/global.png"}},{"activatedV2":true,"order":9,"badge":{"badgeId":"channel","scope":"CHANNEL","imageUrl":"https://nng-phinf.pstatic.net/channel.png"}}]}}""";
        using var document = JsonDocument.Parse(fixture); var profile = ChatProfileParser.Parse(document.RootElement, id, now);
        check(profile.SubscriptionMonths == 12 && profile.ContinuousDonationDays == 3 && profile.FollowedAt is not null, "chat subscription, follow and continuous days");
        check(profile.Badges.Count == 3 && profile.Badges[1].Id == "activity:channel" && profile.Badges.All(x => !x.Id.Contains("hidden")), "active badges and channel ordering");
        check(profile.BelongsTo(new Account("테스트", null, "", null, "", id)) && !profile.BelongsTo(new Account("다른 계정", null, "", null, "", new string('b', 32))), "profile belongs only to active account");
        foreach (var bad in new[] { "https://ssl.pstatic.net.attacker.test/a", "http://ssl.pstatic.net/a", "https://ssl.pstatic.net:444/a", "https://user:pass@ssl.pstatic.net/a", "file:///tmp/a" }) check(!ChatProfileParser.SafeImage(bad), "unsafe badge image rejected");
        foreach (var bad in new[] { "abc", new string('A', 32), id + "\n", "../" }) check(!ChatProfileParser.ValidUserId(bad), "unsafe chat account ID rejected");
        var mismatch = false; try { ChatProfileParser.Parse(document.RootElement, new string('b', 32), now); } catch (InvalidDataException) { mismatch = true; }
        check(mismatch, "mismatched profile response rejected");
        using var emptyModern = JsonDocument.Parse("""{"code":200,"content":{"userIdHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","viewerBadges":[],"activityBadges":[{"activated":true,"imageUrl":"https://ssl.pstatic.net/old.png"}]}}""");
        check(ChatProfileParser.Parse(emptyModern.RootElement, id, now).Badges.Count == 0, "empty modern badges do not resurrect legacy badges");
        using var malformed = JsonDocument.Parse("""{"code":200,"content":{"userIdHash":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","streamingProperty":{"subscription":{"accumulativeMonth":true},"following":{"followDate":"2026-02-30 12:00:00"}}}}""");
        var bounded = ChatProfileParser.Parse(malformed.RootElement, id, now); check(bounded.SubscriptionMonths is null && bounded.FollowedAt is null, "profile rejects boolean counts and impossible dates");
        var requested = new List<(string Host, bool Cookie)>();
        using var api = new ApiClient(new Transport(request =>
        {
            requested.Add((request.RequestUri!.Host, request.Headers.Contains("Cookie")));
            return request.RequestUri.AbsolutePath.Contains("live-status") ? "{\"content\":{\"chatChannelId\":\"test-chat\"}}" : fixture;
        }));
        api.CookieHeader = _ => Task.FromResult("synthetic-session=qa");
        var fetched = await api.ChatProfile(new Account("테스트", null, "", null, "", id));
        check(fetched.UserId == id && requested.Count == 2 && !requested[0].Cookie && requested[1].Cookie, "chat profile uses app-owned cookie only for account request");
    }
    private sealed class Transport(Func<HttpRequestMessage, string> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response(request), Encoding.UTF8, "application/json") });
    }
}
