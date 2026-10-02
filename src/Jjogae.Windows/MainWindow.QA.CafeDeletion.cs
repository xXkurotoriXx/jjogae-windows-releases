using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private async Task VerifyCafeDeletion(Action<bool, string> check)
    {
        var directory = Path.Combine(app.Store.DirectoryPath, "cafe-deletion-fixture");
        var statuses = new Dictionary<string, CafeArticleAvailability>
        { ["123"] = CafeArticleAvailability.Deleted, ["124"] = CafeArticleAvailability.Available, ["125"] = CafeArticleAvailability.Unknown };
        var date = DateTimeOffset.UtcNow;
        CafePost Post(string id) => new(id, "isolated notice " + id, Channel.Name, "공지", true, date, Channel.CafeUrl);
        using var api = new ApiClient(new CafeDeletionFixture(request =>
        {
            if (request.RequestUri!.Host == "article.cafe.naver.com")
            {
                var id = request.RequestUri.AbsolutePath.Split('/').Last();
                return statuses.GetValueOrDefault(id) switch
                {
                    CafeArticleAvailability.Deleted => new(HttpStatusCode.NotFound) { Content = new StringContent("""{"result":{"errorCode":"4003","message":"errorCode: 4003, message: 삭제되었거나 존재하지 않는 게시글입니다.","more":{"cafeId":31522940}}}""") },
                    CafeArticleAvailability.Available => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { result = new { cafeId = 31522940, articleId = id, article = new { id, isReadable = true, isBlind = false } } })) },
                    _ => new(HttpStatusCode.Forbidden) { Content = new StringContent("{}") }
                };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            { result = new { articleList = new[] { "123", "124", "125" }.Select(id => new { articleId = id, subject = "isolated notice " + id, writeDateTimestamp = date.ToUnixTimeMilliseconds() }) } })) };
        }));
        using var controller = new AppController(directory, true, api);
        controller.State.Account = new("isolated cafe account", null, "", null, "");
        controller.State.Settings.Theme = "dark";
        controller.State.Cafe = [Post("123"), Post("125")]; controller.State.ReadCafe = ["123", "125"]; controller.State.SeenCafe = ["123", "125"];
        controller.State.Pending.Add(new("cafe:123", "카페", "새 공지", "isolated notice 123", date, date));
        Policies.RememberNotification(controller.State, "cafe:123"); controller.Save();
        var query = new CafeHistoryQuery(Channel.Today.AddDays(-1), Channel.Today, CafeHistoryScope.Notice);
        await controller.LoadCafeRecovery(query);
        check(controller.RecoveryCandidates?.Articles.Single().Id == "124" && controller.Recovery!.Warning.Length > 0,
            "cafe deletion / production history path hides deleted and denied articles");
        check(controller.State.Cafe.Select(x => x.Id).SequenceEqual(new[] { "125" }) && !controller.State.ReadCafe.Contains("123")
            && !controller.State.SeenCafe.Contains("123") && controller.State.Pending.Count == 0 && controller.State.NotificationKeys.Count == 0,
            "cafe deletion / history purges saved content read seen and pending keys; denied saved article remains");
        controller.RetryNotification(new("cafe:123", "카페", "새 공지", "isolated notice 123", date, date));
        check(controller.State.Pending.Count == 0, "cafe deletion / delayed notification failure cannot retain removed content");
        statuses["124"] = CafeArticleAvailability.Deleted;
        await controller.RestoreCafe(["124"]);
        check(controller.State.Cafe.All(x => x.Id != "124") && controller.Recovery!.Articles.All(x => x.Id != "124"),
            "cafe deletion / deleted after preview cannot be restored from selection");
        await controller.LoadCafeRecovery(query);
        check(controller.Recovery!.Articles.Count == 0 && controller.State.Cafe.All(x => x.Id != "123" && x.Id != "124"),
            "cafe deletion / repeated stale history cannot recreate removed posts");
        var export = Path.Combine(directory, "export.json"); StateStore.Export(controller.Store.Load(), export);
        check(new[] { controller.Store.StatePath, export }.All(path => !File.ReadAllText(path).Contains("isolated notice 123") && !File.ReadAllText(path).Contains("isolated notice 124")),
            "cafe deletion / persisted state and export contain no deleted titles");
        check(controller.Store.Load().Settings.Theme == "dark" && controller.Store.Load().Cafe.Single().Id == "125",
            "cafe deletion / restart preserves unrelated settings and denied existing post");

        statuses["125"] = CafeArticleAvailability.Available;
        await controller.LoadCafeRecovery(query); await controller.RestoreCafe(["125"]);
        check(!controller.State.ReadCafe.Contains("125") && controller.State.Cafe.Single().Id == "125" && controller.State.Pending.Count == 0,
            "cafe deletion / retry after permission recovery restores selected valid post without historical alert");
        controller.ReadCafe(["125"]); await controller.LoadCafeRecovery(query);
        var completion = new TaskCompletionSource<CafeArticleAvailability>();
        controller.CafeAvailabilityTest = (_, _) => completion.Task;
        var restoring = controller.RestoreCafe(["125"]);
        controller.CancelRecovery(); completion.SetResult(CafeArticleAvailability.Available); await restoring;
        controller.CafeAvailabilityTest = null;
        check(controller.State.ReadCafe.Contains("125") && controller.Recovery is null && !controller.RecoveryBusy,
            "cafe deletion / cancellation during restore prevents stale state mutation");

        // A fresh controller has no ten-minute cache: exercise the rotating check
        // against a deleted article that the list API still claims is present.
        using var pollingApi = new ApiClient(new CafeDeletionFixture(request => request.RequestUri!.Host == "article.cafe.naver.com"
            ? new(HttpStatusCode.NotFound) { Content = new StringContent("""{"result":{"errorCode":"4003","reason":"삭제되었거나 존재하지 않는 게시글입니다.","more":{"cafeId":31522940}}}""") }
            : new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { result = new { articleList = new[] { new { articleId = 125, subject = "isolated notice 125", writeDateTimestamp = date.ToUnixTimeMilliseconds() } } } })) }));
        using var polling = new AppController(Path.Combine(directory, "polling"), true, pollingApi);
        polling.State.Account = new("fixture", null, "", null, ""); polling.State.Cafe = [Post("125")];
        polling.SeedRecoveryCheck(new(query, [Post("125")], 1, null, ""));
        await polling.RefreshCafe();
        check(polling.State.Cafe.Count == 0 && polling.Recovery!.Articles.Count == 0, "cafe deletion / listed saved notice is also checked and purged from active preview");
        await polling.RefreshCafe();
        check(polling.State.Cafe.Count == 0 && polling.State.SeenCafe.Count == 0,
            "cafe deletion / subsequent stale normal refresh cannot recreate deleted notice");
    }
    private sealed class CafeDeletionFixture(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(respond(request)); }
    }
}
