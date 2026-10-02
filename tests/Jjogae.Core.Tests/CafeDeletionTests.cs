using System.Net;
using System.Text.Json;
using Jjogae.Core;

static class CafeDeletionTests
{
    private const string Deleted = """{"result":{"errorCode":"4003","reason":"삭제되었거나 존재하지 않는 게시글입니다.","message":"errorCode: 4003, message: 삭제되었거나 존재하지 않는 게시글입니다.","more":{"cafeId":31522940}}}""";
    private const string Available = """{"result":{"cafeId":31522940,"articleId":123,"article":{"id":123,"isReadable":true,"isBlind":false}}}""";
    public static async Task Run(Action<bool, string> check)
    {
        CafeArticleAvailability Parse(string json, int status = 200, string id = "123")
        { using var doc = JsonDocument.Parse(json); return CafeAvailability.Parse(doc.RootElement, status, id); }
        foreach (var status in new[] { 200, 404 })
            check(Parse(Deleted, status) == CafeArticleAvailability.Deleted, "cafe deletion / current Naver reason and prefixed message confirmed");
        var messageOnly = Deleted.Replace("\"reason\":\"삭제되었거나 존재하지 않는 게시글입니다.\",", "");
        check(Parse(messageOnly, 404) == CafeArticleAvailability.Deleted, "cafe deletion / prefixed message works without reason");
        foreach (var status in new[] { 401, 403, 429, 500 })
            check(Parse(Deleted, status) == CafeArticleAvailability.Unknown, "cafe deletion / login and network errors are not deletion");
        check(Parse(Available) == CafeArticleAvailability.Available, "cafe deletion / exact readable article confirmed");
        foreach (var json in new[] { "{}", "null", Available.Replace("31522940", "1"), Available.Replace("123", "124"), Available.Replace("\"isReadable\":true", "\"isReadable\":false"), Available.Replace("\"isBlind\":false", "\"isBlind\":true"), Deleted.Replace("4003", "4004"), Deleted.Replace("31522940", "1") })
            check(Parse(json) == CafeArticleAvailability.Unknown, "cafe deletion / ambiguous or mismatched response never permits restore");

        var at = DateTimeOffset.UtcNow;
        CafePost Post(string id) => new(id, "fixture " + id, Channel.Name, "공지", true, at, Channel.CafeUrl);
        var state = new AppState { Cafe = [Post("123"), Post("124"), Post("125")], ReadCafe = ["123", "124"], SeenCafe = ["123", "124", "125"] };
        Policies.RememberNotification(state, "cafe:124");
        state.Pending.Add(new("cafe:124", "카페", "새 공지", "fixture 124", at, at));
        var verified = await CafeAvailability.Verify(state.Cafe, (id, _) => id == "125" ? throw new HttpRequestException() : Task.FromResult(id == "124" ? CafeArticleAvailability.Deleted : CafeArticleAvailability.Available), observed: (id, status) =>
        { if (status == CafeArticleAvailability.Deleted) CafeAvailability.Purge(state, [id]); });
        check(verified.Available.Select(x => x.Id).SequenceEqual(new[] { "123" }) && verified.UnverifiedCount == 1, "cafe deletion / unknown and deleted omitted from preview");
        check(state.Cafe.Select(x => x.Id).SequenceEqual(new[] { "123", "125" }) && !state.ReadCafe.Contains("124") && !state.SeenCafe.Contains("124") && state.Pending.Count == 0 && state.NotificationKeys.Count == 0, "cafe deletion / purge removes content and every reference; unknown saved post preserved");
        var query = new CafeHistoryQuery(Channel.Today.AddDays(-1), Channel.Today, CafeHistoryScope.Notice);
        var candidates = new CafeRecoveryCandidates(state, query, verified.Available);
        check(candidates.Articles.Single().Id == "123", "cafe deletion / cached unverified posts cannot reenter preview");
        var restored = candidates.Restore(state, ["124", "125"]);
        check(restored == 0 && state.ReadCafe.Contains("123"), "cafe deletion / omitted selections cannot restore");
        var stale = await CafeAvailability.Verify([Post("124")], (_, _) => Task.FromResult(CafeArticleAvailability.Deleted));
        check(new CafeRecoveryCandidates(state, query, stale.Available).Restore(state, ["124"]) == 0 && state.Cafe.All(x => x.Id != "124"), "cafe deletion / stale repeated history cannot resurrect removed content");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var cancelled = false;
        try { await CafeAvailability.Verify([Post("123")], (_, _) => throw new Exception("must not request"), cancellation.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "cafe deletion / cancelled verification never presents articles");
        var many = new AppState { Cafe = Enumerable.Range(200, 12).Select(id => Post(id.ToString())).ToList() };
        var batchResult = await CafeAvailability.Verify(many.Cafe, (_, _) => Task.FromResult(CafeArticleAvailability.Deleted), observed: (id, _) => CafeAvailability.Purge(many, [id]));
        check(many.Cafe.Count == 0 && batchResult.Available.Count == 0, "cafe deletion / purging live state across several batches never invalidates enumeration");
        var timeoutResult = await CafeAvailability.Verify([Post("126"), Post("127")], (id, _) => id == "126" ? throw new TaskCanceledException("isolated timeout") : throw new JsonException("isolated malformed response"));
        check(timeoutResult.UnverifiedCount == 2 && timeoutResult.Available.Count == 0, "cafe deletion / timed-out and malformed responses stay unavailable for restoration");

        foreach (var status in new[] { HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
        {
            using var api = new ApiClient(new Fixture(_ => new(status) { Content = new StringContent(status == HttpStatusCode.OK ? Available : Deleted) }));
            var result = await api.CafeAvailabilityCheck("123");
            check(result == (status == HttpStatusCode.OK ? CafeArticleAvailability.Available : status == HttpStatusCode.NotFound ? CafeArticleAvailability.Deleted : CafeArticleAvailability.Unknown), "cafe deletion / transport distinguishes readable, deleted, and denied");
        }
        var directory = Path.Combine(Path.GetTempPath(), "Jjogae-CafeDeletion-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new StateStore(directory); store.Save(state);
            var export = Path.Combine(directory, "export.json"); StateStore.Export(store.Load(), export);
            foreach (var path in new[] { store.StatePath, export })
                check(!File.ReadAllText(path).Contains("124") && !File.ReadAllText(path).Contains("fixture 124"), "cafe deletion / restart and export retain no removed identifier or title");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
