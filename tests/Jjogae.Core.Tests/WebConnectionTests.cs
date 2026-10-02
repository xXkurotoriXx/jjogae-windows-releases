using System.Net;
using System.Text.Json;
using Jjogae.Core;

static class WebConnectionTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        YouTubeWebObservation? Read(string fields) { using var doc = JsonDocument.Parse(fields); return YouTubeWebObservation.Decode(doc.RootElement, now); }
        var active = Read($$"""{"channelID":"{{Channel.YouTubeId}}","loggedIn":true,"subscribed":true,"active":true,"months":6,"tier":"루파","billing":"10월 1일"}""")!;
        check(active is { Subscribed: true, MembershipActive: true, MembershipMonths: 6, Tier: "루파" }, "web observation decodes target channel only");
        foreach (var json in new[] { "null", "{}", "[]", "{\"loggedIn\":true}", $$"""{"channelID":"{{Channel.YouTubeId}}","loggedIn":false,"active":false}""", "{\"channelID\":\"other\",\"loggedIn\":true,\"active\":true}" })
            check(Read(json) is null, "unknown or logged-out page does not overwrite observations");
        var partial = Read($$"""{"channelID":"{{Channel.YouTubeId}}","loggedIn":true,"subscribed":false}""")!;
        var loginOnly = Read($$"""{"channelID":"{{Channel.YouTubeId}}","loggedIn":true}""");
        check(loginOnly is { Subscribed: null, MembershipActive: null }, "login survives missing subscription controls");
        check(Read($$"""{"channelID":"{{Channel.YouTubeId}}","loggedIn":true,"subscribed":true,"subscriberText":"구독자 6.94천명"}""")?.Subscribers == 6940, "web subscriber label uses public count parser");
        check(active.Merge(loginOnly!) is { Subscribed: true, MembershipActive: true, MembershipMonths: 6 }, "login-only observation preserves known subscription");
        check(active.Merge(partial) is { Subscribed: false, MembershipMonths: 6 }, "partial observation preserves membership details");
        var inactive = Read($$"""{"channelID":"{{Channel.YouTubeId}}","loggedIn":true,"active":false,"months":7,"tier":"stale"}""")!;
        check(active.Merge(inactive) is { MembershipActive: false, MembershipMonths: null, Tier: null, NextBillingLabel: null }, "inactive observation clears membership details");
        foreach (var months in new[] { "-1", "1201", "6.5", "\"6\"" })
            check(Read($$"""{"channelID":"{{Channel.YouTubeId}}","loggedIn":true,"active":true,"months":{{months}}}""")?.MembershipMonths is null, "invalid months rejected without estimating start date");
        foreach (var url in new[] { "https://accounts.google.com/", YouTubeWebPolicy.Membership, YouTubeWebPolicy.PaidMemberships }) check(YouTubeWebPolicy.Allows(url), "expected web destinations allowed");
        foreach (var url in new[] { "https://accounts.google.co.kr/", "https://www.google.co.kr/accounts/SetSID" }) check(YouTubeWebPolicy.Allows(url), "Korean Google account destinations allowed");
        foreach (var url in new[] { "https://accounts.google.co.kr.evil.test/", "https://www.google.co.kr/search", "http://accounts.google.co.kr/", "https://user@accounts.google.co.kr/" }) check(!YouTubeWebPolicy.Allows(url), "regional account policy remains scoped");
        check(!YouTubeWebPolicy.CanObserve("https://accounts.google.co.kr/"), "regional authentication pages are never scraped");
        foreach (var url in new[] { "https://accounts.google.co.in/", "https://accounts.google.co.in/accounts/SetSID", "https://www.google.co.in/accounts/SetSID" })
        {
            check(YouTubeWebPolicy.Allows(url), "Indian Google account continuation allowed");
            check(!YouTubeWebPolicy.CanObserve(url), "Indian authentication pages are never scraped");
        }
        foreach (var url in new[] { "https://accounts.google.co.in.evil.test/", "https://accounts.google.co.invalid/", "https://www.google.co.in/search", "https://www.google.co.in/accounts-evil/", "https://evil.google.co.in/", "http://accounts.google.co.in/", "https://user@accounts.google.co.in/", "https://accounts.google.co.in:444/" })
            check(!YouTubeWebPolicy.Allows(url), "Indian account policy remains scoped");
        foreach (var url in new[] { "http://www.youtube.com/", "https://www.youtube.com.evil.test/", "https://user@www.youtube.com/", "https://www.youtube.com:444/", "file:///tmp/file", "javascript:alert(1)" }) check(!YouTubeWebPolicy.Allows(url), "foreign web destinations blocked");
        check(!YouTubeWebPolicy.CanObserve("https://accounts.google.com/") && !YouTubeWebPolicy.CanObserve("https://www.youtube.com/watch?v=123") && YouTubeWebPolicy.CanObserve(YouTubeWebPolicy.Membership), "DOM read restricted to target membership pages");
        check(YouTubeWebPolicy.CanObserve(Channel.YouTubeUrl) && YouTubeWebPolicy.CanObserve("https://www.youtube.com/@rupa/membership") && YouTubeWebPolicy.CanObserve("https://www.youtube.com/"), "channel redirects and login landing can be inspected with DOM channel validation");
        const string deleted = """{"result":{"errorCode":"4003","message":"삭제되었거나 존재하지 않는 게시글입니다.","more":{"cafeId":31522940}}}""";
        using var deletion = JsonDocument.Parse(deleted);
        foreach (var status in new[] { 200, 404, 401, 403, 500 }) check(CafeAvailability.IsDeleted(deletion.RootElement, status) == (status is 200 or 404), "only explicit service deletion confirms removal");
        foreach (var json in new[] { "{}", "null", deleted.Replace("31522940", "123"), deleted.Replace("4003", "4004"), deleted.Replace("삭제되었거나 존재하지 않는 게시글입니다.", "로그인이 필요합니다.") })
        { using var doc = JsonDocument.Parse(json); check(!CafeAvailability.IsDeleted(doc.RootElement, 404), "ambiguous deletion response retained"); }
        foreach (var id in new[] { "", "0", "-1", "1/2", "123?x=2", "9999999999999999" }) check(CafeAvailability.Url(id) is null, "invalid article identifiers rejected");

        var calls = new List<string>();
        using var api = new ApiClient(new Handler(request =>
        {
            calls.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.Host == "article.cafe.naver.com") return new(HttpStatusCode.NotFound) { Content = new StringContent(deleted) };
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"result":{"articleList":[{"articleId":123,"subject":"공지 제목","writeDateTimestamp":1788220800000}]}}""") };
        }));
        api.CookieHeader = uri => { check(uri.Scheme == "https" && uri.Host.EndsWith("naver.com"), "only Naver-owned session requested for cafe"); return Task.FromResult(""); };
        var notices = await api.Cafe();
        check(notices.Count == 1 && notices.All(x => x.Notice), "notice board and pinned notices merged");
        check(calls.Count == 2 && calls.Any(x => x.Contains("/menus/22/articles")) && calls.All(x => !x.Contains("/menus/0/articles")), "ordinary cafe board is not fetched");
        check(await api.CafeDeleted("123"), "explicit 404 deletion processed through HTTP client");
        var state = new AppState { Cafe = notices, ReadCafe = ["123"], SeenCafe = ["123"], YouTubeWeb = active };
        Policies.QueueNotification(state, new("cafe:123", "카페", "새 공지", "공지 제목", now, now), false);
        state.Pending.Add(new("cafe:123", "카페", "새 공지", "공지 제목", now, now));
        CafeAvailability.Purge(state, ["123"]);
        check(state.Cafe.Count == 0 && state.ReadCafe.Count == 0 && state.SeenCafe.Count == 0 && state.NotificationKeys.Count == 0 && state.Pending.Count == 0, "deletion purges all internal cafe references without tombstones");
        var directory = Path.Combine(Path.GetTempPath(), "jjogae-web-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new StateStore(directory); store.Save(state);
            var reloaded = store.Load();
            check(reloaded.Cafe.Count == 0 && reloaded.YouTubeWeb == active, "purged notices stay gone after reload and web observation persists");
            var export = Path.Combine(directory, "export.json"); StateStore.Export(state, export);
            check(!File.ReadAllText(export).Contains("공지 제목") && !File.ReadAllText(export).Contains("activities"), "export excludes deleted content and notification history");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
