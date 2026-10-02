namespace Jjogae.Core;

public enum CafeHistoryScope { All, Rupa, Notice }
public sealed record CafeHistoryQuery(DateOnly Start, DateOnly End, CafeHistoryScope Scope = CafeHistoryScope.All)
{
    public bool ContainsDate(CafePost post) { var day = RecordCalendar.Day(post.At); return day >= Start && day <= End; }
    public bool Matches(CafePost post) => ContainsDate(post) && (Scope == CafeHistoryScope.All || Scope == CafeHistoryScope.Rupa && post.Rupa || Scope == CafeHistoryScope.Notice && post.Notice);
    public void Validate()
    {
        if (Start > End || Start < new DateOnly(2000, 1, 1) || End > Channel.Today)
            throw new InvalidOperationException("조회 날짜 범위를 확인해 주세요.");
    }
}
public sealed record CafeHistoryPage(IReadOnlyList<CafePost> Articles, long? TotalCount = null);
public sealed record CafeHistoryResult(CafeHistoryQuery Query, IReadOnlyList<CafePost> Articles, int PagesFetched, int? NextPage, string Warning);

public static class CafeHistoryLoader
{
    public static async Task<CafeHistoryResult> Fetch(CafeHistoryQuery query, int firstPage,
        Func<int, CancellationToken, Task<CafeHistoryPage>> fetchPage, CancellationToken token = default, int delayMilliseconds = 100)
    {
        query.Validate(); firstPage = Math.Clamp(firstPage, 1, 100_000);
        var found = new Dictionary<string, CafePost>(); var seen = new HashSet<string>(); var fetched = 0;
        CafeHistoryResult Result(int? next = null, string warning = "") => new(query, found.Values.OrderByDescending(x => x.At).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray(), fetched, next, warning);
        for (var page = firstPage; page < firstPage + 50; page++)
        {
            token.ThrowIfCancellationRequested(); CafeHistoryPage response;
            try { response = await fetchPage(page, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return Result(page, "일부 페이지를 불러오지 못했습니다. 이어서 조회할 수 있습니다."); }
            token.ThrowIfCancellationRequested(); fetched++;
            var unique = response.Articles.Where(x => seen.Add(x.Id)).ToArray();
            if (response.Articles.Count > 0 && unique.Length == 0) return Result(null, "같은 글 목록이 반복되어 조회를 멈췄습니다.");
            foreach (var article in unique.Where(query.ContainsDate)) found[article.Id] = article;
            var done = response.Articles.Count == 0 || response.Articles.All(x => RecordCalendar.Day(x.At) < query.Start)
                || (response.TotalCount is { } total ? page >= Math.Ceiling(total / 20d) : response.Articles.Count < 20);
            if (done) return Result();
            if (delayMilliseconds > 0 && page < firstPage + 49) await Task.Delay(delayMilliseconds, token);
        }
        return Result(firstPage + 50, "50페이지까지 확인했습니다. 이전 글은 이어서 조회할 수 있습니다.");
    }
}

public sealed class CafeRecoveryCandidates
{
    public CafePost[] Articles { get; }
    public int AlreadyVisible { get; }
    public int NewCount { get; }
    public int SavedReadCount => Articles.Length - NewCount;
    public CafeRecoveryCandidates(AppState state, CafeHistoryQuery query, IEnumerable<CafePost> fetched)
    {
        var saved = state.Cafe.Select(x => x.Id).ToHashSet();
        var combined = fetched.Concat(state.Cafe).GroupBy(x => x.Id)
            .Select(g => g.First() with { Notice = g.Any(x => x.Notice) }).Where(query.Matches).ToArray();
        AlreadyVisible = combined.Count(x => saved.Contains(x.Id) && !state.ReadCafe.Contains(x.Id));
        Articles = combined.Where(x => !saved.Contains(x.Id) || state.ReadCafe.Contains(x.Id)).OrderByDescending(x => x.At).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        NewCount = Articles.Count(x => !saved.Contains(x.Id));
    }
    public int Restore(AppState state, IEnumerable<string> selected)
    {
        var ids = selected.ToHashSet(); var posts = Articles.Where(x => ids.Contains(x.Id)).ToArray();
        Policies.MergeCafe(state, posts, DateTimeOffset.UtcNow, true);
        return posts.Length;
    }
}
