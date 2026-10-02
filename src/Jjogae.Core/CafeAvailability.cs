using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jjogae.Core;

public enum CafeArticleAvailability { Unknown, Available, Deleted }
public sealed record CafeVerifiedArticles(IReadOnlyList<CafePost> Available, int UnverifiedCount);

public static class CafeAvailability
{
    public static string? Url(string id) => id.Length is > 0 and <= 15 && id.All(c => c is >= '0' and <= '9') && long.TryParse(id, out var number) && number > 0
        ? $"https://article.cafe.naver.com/gw/v4/cafes/{Channel.CafeId}/articles/{id}?useCafeId=true&requestFrom=A" : null;
    public static bool IsDeleted(JsonElement root, int status)
    {
        if (status != 404 && (status < 200 || status >= 300)) return false;
        var result = J.At(root, "result");
        const string reason = "삭제되었거나 존재하지 않는 게시글입니다.";
        return J.String(result, "errorCode") == "4003"
            && (J.String(result, "reason") == reason || J.String(result, "message") == reason
                || J.String(result, "message") == "errorCode: 4003, message: " + reason)
            && J.String(J.At(result, "more"), "cafeId") == Channel.CafeId;
    }
    public static CafeArticleAvailability Parse(JsonElement root, int status, string id)
    {
        if (Url(id) is null) return CafeArticleAvailability.Unknown;
        if (IsDeleted(root, status)) return CafeArticleAvailability.Deleted;
        if (status != 200) return CafeArticleAvailability.Unknown;
        var result = J.At(root, "result"); var article = J.At(result, "article");
        return J.String(result, "errorCode").Length == 0 && J.String(result, "cafeId") == Channel.CafeId
            && J.String(result, "articleId") == id && J.String(article, "id") == id
            && J.Bool(J.At(article, "isReadable")) && !J.Bool(J.At(article, "isBlind"))
            ? CafeArticleAvailability.Available : CafeArticleAvailability.Unknown;
    }
    public static async Task<CafeVerifiedArticles> Verify(IEnumerable<CafePost> articles,
        Func<string, CancellationToken, Task<CafeArticleAvailability>> check, CancellationToken token = default,
        Action<string, CafeArticleAvailability>? observed = null)
    {
        var available = new List<CafePost>(); var unverified = 0;
        foreach (var batch in articles.DistinctBy(x => x.Id).ToArray().Chunk(4))
        {
            token.ThrowIfCancellationRequested();
            var results = await Task.WhenAll(batch.Select(async post =>
            {
                try { return await check(post.Id, token); }
                catch (Exception) when (!token.IsCancellationRequested) { return CafeArticleAvailability.Unknown; }
            }));
            token.ThrowIfCancellationRequested();
            for (var index = 0; index < batch.Length; index++)
            {
                observed?.Invoke(batch[index].Id, results[index]);
                if (results[index] == CafeArticleAvailability.Available) available.Add(batch[index]);
                else if (results[index] == CafeArticleAvailability.Unknown) unverified++;
            }
        }
        return new(available, unverified);
    }
    public static void Purge(AppState state, IEnumerable<string> deleted)
    {
        var ids = deleted.ToHashSet();
        state.Cafe.RemoveAll(x => ids.Contains(x.Id));
        state.ReadCafe.ExceptWith(ids); state.SeenCafe.ExceptWith(ids);
        state.Pending.RemoveAll(x => x.Source == "카페" && ids.Contains(x.Id.StartsWith("cafe:") ? x.Id[5..] : ""));
        foreach (var id in ids) state.NotificationKeys.Remove(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("cafe:" + id))));
    }
}
