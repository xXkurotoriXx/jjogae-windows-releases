using System.Net;
using System.Text.Json;
using Jjogae.Core;

static class BroadcastAvailabilityTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var now = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        Broadcast Replay(string id = "123") => new() { Id = "vod:" + id, VideoId = id, LiveSessionId = "id-session", Title = "recent replay",
            PublishedAt = now.AddDays(-2), StartedAt = now.AddDays(-2).AddHours(-1), Seconds = 3600, VodSeconds = 3600 };
        ReplayAvailability Parse(string json, int status = 404) { using var doc = JsonDocument.Parse(json); return BroadcastAvailability.Parse(doc.RootElement, status, "123"); }
        const string missing = """{"code":404,"message":"동영상 정보가 존재하지 않습니다."}""";
        // Actual response observed for the user's unavailable video 15326326.
        const string unavailable = """{"code":400,"message":"이용할 수 없는 동영상입니다."}""";
        var available = JsonSerializer.Serialize(new { code = 200, content = new { videoNo = 123, videoType = "REPLAY", channel = new { channelId = Channel.Id } } });
        check(Parse(missing) == ReplayAvailability.Unavailable, "replay explicit absent response");
        check(Parse(unavailable, 400) == ReplayAvailability.Unavailable, "replay unavailable/private response for video 15326326");
        check(Parse(unavailable, 403) == ReplayAvailability.Unknown, "replay permission error is never private-video confirmation");
        check(Parse(available, 200) == ReplayAvailability.Available, "replay exact channel and video verified");
        foreach (var json in new[] { "{}", "null", """{"code":404,"message":"접근 권한이 없습니다."}""", missing.Replace("404", "500"), missing.Replace(".", "?"), missing[..^1] + ",\"content\":{}}" })
            check(Parse(json) == ReplayAvailability.Unknown, "replay ambiguous response retains record / " + json);
        foreach (var status in new[] { 200, 401, 403, 429, 500, 502 }) check(Parse(missing, status) == ReplayAvailability.Unknown, "replay HTTP error not deletion / " + status);
        check(Parse(available.Replace(Channel.Id, "other"), 200) == ReplayAvailability.Unknown, "replay foreign channel rejected");
        check(Parse(available.Replace("123", "456"), 200) == ReplayAvailability.Unknown, "replay mismatched video rejected");
        foreach (var id in new[] { "", "../123", "123?x=y", "-1", "12a", new string('1', 101) }) check(BroadcastAvailability.Url(id) is null, "replay invalid ID blocked");

        var recent = Replay();
        var manual = new Broadcast { Id = "manual:keep", Title = "manual", StartedAt = recent.StartedAt!.Value.AddMinutes(20), Seconds = 1800 };
        var old = Replay("789") with { PublishedAt = now.AddDays(-8), LiveSessionId = "id-old" };
        var state = new AppState { Broadcasts = [recent, manual, old], Media = [new("123", "치지직", "다시보기", "recent", "", "", recent.PublishedAt), new("keep", "YouTube", "새 영상", "keep", "", "", now)], Cheese = [new("keep", "rupa", 1, "", "", now)] };
        check(BroadcastAvailability.Candidates(state, now).SequenceEqual(["123"]), "replay recent window leaves old and manual untouched");
        check(BroadcastAvailability.Observe(state, "123", ReplayAvailability.Unavailable, now).Count == 0 && state.Broadcasts.Count == 3, "replay first deletion retains record");
        check(BroadcastAvailability.Candidates(state, now.AddSeconds(59)).Length == 0, "replay checks follow default refresh interval");
        check(BroadcastAvailability.Observe(state, "123", ReplayAvailability.Unavailable, now.AddSeconds(59)).Count == 0, "replay immediate second response does not remove");
        // A refresh merge and app restart must preserve the first confirmation.
        Policies.MergeBroadcast(state, Replay());
        state = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state, StateStore.Json), StateStore.Json)!;
        state.Normalize();
        check(state.Broadcasts.Single(x => x.VideoId == "123").ReplayMissingSince == now, "replay confirmation survives merge and restart");
        state.Pending.Add(new("media:치지직:123", "치지직", "다시보기", "recent", now, now));
        state.Pending.Add(new("keep", "YouTube", "새 영상", "keep", now, now));
        var removed = BroadcastAvailability.Observe(state, "123", ReplayAvailability.Unavailable, now.AddMinutes(1));
        check(removed.Count == 1 && state.Broadcasts.Count == 2 && state.Broadcasts.Any(x => x.Id == manual.Id), "replay confirmed removal preserves manual and old");
        check(state.Media.Count == 1 && state.Pending.Count == 1 && state.Cheese.Count == 1, "replay removal scoped to matching media and pending alert");
        check(Policies.DailySeconds(state.Broadcasts).Values.Sum() == 3600, "replay removed from duration statistics with overlap preserved");
        Policies.MergeBroadcast(state, Replay());
        Policies.MergeBroadcast(state, new() { Id = "live:session", LiveSessionId = "id-session", StartedAt = recent.StartedAt, Seconds = 3600 });
        Policies.ObserveMedia(state, [new("123", "치지직", "다시보기", "cached", "", "", now)], null, null, null, now);
        check(state.Broadcasts.Count == 2 && state.Media.All(x => x.Id != "123"), "replay stale API cannot reinsert removed records");
        var reloaded = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state, StateStore.Json), StateStore.Json)!;
        reloaded.Normalize(); Policies.MergeBroadcast(reloaded, Replay());
        check(reloaded.Broadcasts.Count == 2 && reloaded.DeletedReplayIds.Contains("123"), "replay suppression survives restart");

        foreach (var observation in new[] { ReplayAvailability.Unknown, ReplayAvailability.Available })
        {
            var reset = new AppState { Broadcasts = [Replay()] };
            BroadcastAvailability.Observe(reset, "123", ReplayAvailability.Unavailable, now);
            BroadcastAvailability.Observe(reset, "123", observation, now.AddMinutes(1));
            BroadcastAvailability.Observe(reset, "123", ReplayAvailability.Unavailable, now.AddMinutes(2));
            check(reset.Broadcasts.Count == 1 && reset.Broadcasts[0].ReplayMissingSince == now.AddMinutes(2), "replay unknown or recovery resets deletion evidence / " + observation);
        }
        var split = new AppState { Broadcasts = [Replay(), Replay("456"), new() { Id = "live:session", LiveSessionId = "id-session", StartedAt = recent.StartedAt, Seconds = 3600 }] };
        BroadcastAvailability.Observe(split, "123", ReplayAvailability.Unavailable, now);
        BroadcastAvailability.Observe(split, "123", ReplayAvailability.Unavailable, now.AddMinutes(1));
        check(split.Broadcasts.Count == 2 && split.DeletedBroadcastSessions.Count == 0, "replay split video and independent live record retained");
        var orphan = new AppState { Broadcasts = [Replay(), new() { Id = "live:session", LiveSessionId = "id-session", StartedAt = recent.StartedAt, Seconds = 3600 }] };
        BroadcastAvailability.Observe(orphan, "123", ReplayAvailability.Unavailable, now);
        check(BroadcastAvailability.Observe(orphan, "123", ReplayAvailability.Unavailable, now.AddMinutes(1)).Count == 2 && orphan.Broadcasts.Count == 0, "replay orphan live duplicate removed with video");
        var clock = new AppState { Broadcasts = [Replay()] };
        BroadcastAvailability.Observe(clock, "123", ReplayAvailability.Unavailable, now);
        check(BroadcastAvailability.Observe(clock, "123", ReplayAvailability.Unavailable, now.AddMinutes(-10)).Count == 0, "replay clock rollback retains records");
        BroadcastAvailability.Observe(clock, "123", ReplayAvailability.Unavailable, now.AddDays(8));
        check(clock.Broadcasts.Count == 1 && clock.Broadcasts[0].ReplayMissingSince == now.AddDays(8), "replay expired evidence starts over");
        var many = new AppState { Broadcasts = Enumerable.Range(1, 10).Select(x => Replay(x.ToString())).ToList() };
        check(BroadcastAvailability.Candidates(many, now).Length == 3, "replay requests bounded per refresh");
        foreach (var seconds in new[] { 60, 300, 600, 3600 })
        {
            var configured = new AppState { Settings = new() { RefreshSeconds = seconds }, Broadcasts = [Replay()] };
            BroadcastAvailability.Observe(configured, "123", ReplayAvailability.Unavailable, now);
            check(BroadcastAvailability.Candidates(configured, now.AddSeconds(seconds - 1)).Length == 0, "replay respects configured interval before next refresh / " + seconds);
            check(BroadcastAvailability.Observe(configured, "123", ReplayAvailability.Unavailable, now.AddSeconds(seconds - 1)).Count == 0, "replay cannot confirm before configured interval / " + seconds);
            check(BroadcastAvailability.Candidates(configured, now.AddSeconds(seconds)).SequenceEqual(["123"]), "replay due at configured refresh interval / " + seconds);
            check(BroadcastAvailability.Observe(configured, "123", ReplayAvailability.Unavailable, now.AddSeconds(seconds)).Count == 1, "replay confirms on next configured refresh / " + seconds);
        }
        var changedInterval = new AppState { Settings = new() { RefreshSeconds = 600 }, Broadcasts = [Replay()] };
        BroadcastAvailability.Observe(changedInterval, "123", ReplayAvailability.Unavailable, now);
        changedInterval.Settings.RefreshSeconds = 60;
        check(BroadcastAvailability.Candidates(changedInterval, now.AddMinutes(1)).SequenceEqual(["123"]), "replay new refresh selection takes effect immediately");

        foreach (var (status, body, expected) in new[] { (404, missing, ReplayAvailability.Unavailable), (400, unavailable, ReplayAvailability.Unavailable), (200, available, ReplayAvailability.Available), (403, missing, ReplayAvailability.Unknown), (500, missing, ReplayAvailability.Unknown) })
        {
            using var api = new ApiClient(new Handler(request =>
            {
                check(request.RequestUri!.ToString() == BroadcastAvailability.Url("123") && !request.Headers.Contains("Cookie") && request.Headers.Authorization is null, "replay probe fixed public URL with no credentials");
                return new((HttpStatusCode)status) { Content = new StringContent(body) };
            }));
            api.CookieHeader = _ => throw new Exception("Probe must not request cookies");
            check(await api.BroadcastAvailabilityCheck("123") == expected, "replay probe response / " + status);
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var canceledApi = new ApiClient(new Handler(_ => new(HttpStatusCode.OK)));
        var propagated = false;
        try { await canceledApi.BroadcastAvailabilityCheck("123", canceled.Token); } catch (OperationCanceledException) { propagated = true; }
        check(propagated, "replay cancellation propagated");
        foreach (var sessionCookies in new[] { "", "fixture=value" })
        {
            var calls = 0; var cookieLookups = 0;
            using var api = new ApiClient(new Handler(request =>
            {
                calls++;
                check(!request.Headers.Contains("Cookie") && request.Headers.Authorization is null, "replay visibility remains public with an app session");
                // A privileged follow-up would see the video, but must never occur.
                return new((HttpStatusCode)(calls == 1 ? 400 : 200)) { Content = new StringContent(calls == 1 ? unavailable : available) };
            }));
            api.CookieHeader = _ => { cookieLookups++; return Task.FromResult(sessionCookies); };
            check(await api.BroadcastAvailabilityCheck("123") == ReplayAvailability.Unavailable && calls == 1 && cookieLookups == 0,
                "replay public unavailability wins regardless of valid or expired app login");
            var connected = new AppState { Account = new("fixture", null, "", null, ""), Broadcasts = [Replay()] };
            BroadcastAvailability.Observe(connected, "123", ReplayAvailability.Unavailable, now);
            BroadcastAvailability.Observe(connected, "123", ReplayAvailability.Unavailable, now.AddMinutes(1));
            check(connected.Broadcasts.Count == 0, "replay public unavailability removes records while account is connected");
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
