using System.Security.Cryptography;
using System.Text;

namespace Jjogae.Core;

public static class Policies
{
    public static IReadOnlyList<T> Page<T>(IReadOnlyList<T> items, int page, int size)
    {
        if (!new[] { 10, 20, 50, 100 }.Contains(size)) size = 10;
        var last = Math.Max(0, (items.Count - 1) / size);
        return items.Skip(Math.Clamp(page, 0, last) * size).Take(size).ToArray();
    }

    public static long Milestone(long count)
    {
        if (count < 1000) return 0;
        var step = count < 10_000 ? 1000 : count < 100_000 ? 5000 : count < 1_000_000 ? 10_000 : 100_000;
        return count / step * step;
    }

    public static bool RememberNotification(AppState state, string id) =>
        state.NotificationKeys.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))));

    public static void QueueNotification(AppState state, Notification entry, bool notify)
    {
        if (!RememberNotification(state, entry.Id)) return;
        if (notify && state.Settings.Notifications) state.Pending.Add(entry);
    }

    public static void MergeCafe(AppState state, IEnumerable<CafePost> posts, DateTimeOffset now, bool restoring = false)
    {
        var incoming = posts.GroupBy(x => x.Id).Select(g => g.First() with { Notice = g.Any(x => x.Notice) }).ToArray();
        var known = state.SeenCafe.Union(state.Cafe.Select(x => x.Id)).ToHashSet();
        // Lists can be cached longer than a polling interval. Deduplicate by ID,
        // rather than permanently discarding a late-arriving recent article.
        var floor = now.AddHours(-24);
        foreach (var article in incoming.OrderBy(x => x.At))
        {
            if (!restoring && state.CafeBaseline && !known.Contains(article.Id) && article.Protected
                && article.At >= floor && article.At <= now.AddMinutes(2))
                QueueNotification(state, new Notification("cafe:" + article.Id, "카페", article.Notice ? "새 공지" : "루파 글",
                    article.Title, article.At, now, article.Url, "service"), true);
            known.Add(article.Id);
        }
        state.SeenCafe.UnionWith(known);
        var merged = state.Cafe.ToDictionary(x => x.Id);
        foreach (var article in incoming)
            merged[article.Id] = merged.TryGetValue(article.Id, out var previous) ? article with { Notice = article.Notice || previous.Notice } : article;
        state.Cafe = merged.Values.OrderByDescending(x => x.At).ToList();
        if (!restoring) { state.CafeBaseline = true; state.CafeCheckedAt = now; }
        else state.ReadCafe.ExceptWith(incoming.Select(x => x.Id));
    }

    public static void MarkCafeRead(AppState state, IEnumerable<string> ids)
    {
        var selected = ids.ToHashSet();
        state.ReadCafe.UnionWith(selected);
        state.Pending.RemoveAll(x => x.Source == "카페" && x.Id.StartsWith("cafe:", StringComparison.Ordinal) && selected.Contains(x.Id["cafe:".Length..]));
    }

    public static void OpenCafe(AppState state, CafePost post)
    {
        if (!post.Protected) MarkCafeRead(state, [post.Id]);
    }

    public static void MergeBroadcast(AppState state, Broadcast record)
    {
        if (record.VideoId is { } id && state.DeletedReplayIds.Contains(id)) return;
        if (record.LiveSessionId is { } session)
        {
            if (record.VideoId is not null) state.DeletedBroadcastSessions.Remove(session);
            else if (state.DeletedBroadcastSessions.Contains(session)) return;
        }
        state.Broadcasts = BroadcastLogic.Merge(state.Broadcasts, record);
        var merged = state.Broadcasts.FirstOrDefault(x => BroadcastLogic.Same(x, record));
        if (merged is not { StartedAt: { } start } || !BroadcastLogic.ValidDuration(merged.Seconds) || merged.TimingSource == "observedEnd") return;
        var end = merged.EndedAt ?? start.AddSeconds(merged.Seconds);
        // Reconcile a pending end event after returning from offline without sending the alert again.
        for (var i = 0; i < state.Pending.Count; i++)
        {
            var entry = state.Pending[i];
            if (entry.Source != "치지직" || entry.Kind != "방송 종료" || entry.TimeBasis != "observed") continue;
            if (!entry.Id.EndsWith(":" + start.ToString("O"), StringComparison.Ordinal)) continue;
            var updated = entry with { At = end, Title = merged.Title + " · " + Channel.Duration(merged.Seconds), TimeBasis = merged.TimingSource == "liveTiming" ? "service" : "replay" };
            state.Pending[i] = updated;
        }
    }

    public static Dictionary<DateOnly, double> DailySeconds(IEnumerable<Broadcast> broadcasts) => BroadcastLogic.DailySeconds(broadcasts);

    public static void ObserveMedia(AppState state, IEnumerable<Media> media, LiveObservation? live, long? followers, long? subscribers, DateTimeOffset now)
    {
        media = media.Where(x => x.Source != "치지직" || x.Kind != "다시보기" || !state.DeletedReplayIds.Contains(x.Id));
        var previousMedia = state.Media.DistinctBy(x => $"{x.Source}:{x.Id}").ToDictionary(x => $"{x.Source}:{x.Id}");
        media = media.Select(x => x.PublishedAt is null && previousMedia.TryGetValue($"{x.Source}:{x.Id}", out var previous)
            ? x with { PublishedAt = previous.PublishedAt } : x).ToArray();
        var known = state.Media.Select(x => $"{x.Source}:{x.Id}").ToHashSet();
        foreach (var item in media.OrderBy(x => x.PublishedAt))
        {
            if (item.PublishedAt is { } published)
            {
                var index = state.Pending.FindIndex(x => x.Id == $"media:{item.Source}:{item.Id}" && x.TimeBasis == "observed");
                if (index >= 0) state.Pending[index] = state.Pending[index] with { At = published, TimeBasis = "service" };
            }
            if (item.Kind != "클립" && state.MediaBaseline && known.Add($"{item.Source}:{item.Id}"))
                QueueNotification(state, new Notification($"media:{item.Source}:{item.Id}", item.Source, item.Kind, item.Title, item.PublishedAt ?? now, now, item.Url, item.PublishedAt is null ? "observed" : "service"), item.PublishedAt is null || item.PublishedAt >= now.AddHours(-24));
        }
        state.Media = media.Concat(state.Media).DistinctBy(x => $"{x.Source}:{x.Id}").Take(150).ToList();
        if (live is not null)
        {
            var previous = state.Live;
            if (live.IsLive && previous?.IsLive == true && live.Id == previous.Id && live.StartedAt is null) live = live with { StartedAt = previous.StartedAt };
            if (live.IsLive && (!state.MediaBaseline || previous?.IsLive != true || live.Id != previous.Id || live.StartedAt != previous.StartedAt))
            {
                QueueNotification(state, new Notification($"live:start:{live.Id}:{live.StartedAt:O}", "치지직", "방송 시작", live.Title,
                    live.StartedAt ?? now, now, Channel.Url, live.StartedAt is null ? "observed" : "service"), state.MediaBaseline);
                if (live.StartedAt is { } knownStart) MergeBroadcast(state, new Broadcast { Id = "live:" + live.Id + ":" + knownStart.ToString("O"),
                    LiveSessionId = live.Id.Length > 0 ? "id-" + live.Id : null, Title = live.Title, StartedAt = knownStart, ImageUrl = live.ImageUrl, Url = Channel.Url, TimingSource = "pending" });
            }
            if (!live.IsLive && previous?.IsLive == true)
            {
                var start = live.StartedAt ?? previous.StartedAt;
                var ending = live.EndedAt;
                // A real service end timestamp wins. When it is missing after
                // sleep/offline, wait for a replay duration instead of counting
                // the whole offline gap as broadcast time.
                var duration = start is not null && ending >= start ? (ending.Value - start.Value).TotalSeconds : (double?)null;
                QueueNotification(state, new Notification($"live:end:{previous.Id}:{previous.StartedAt:O}", "치지직", "방송 종료",
                    previous.Title + (duration is null ? "" : " · " + Channel.Duration(duration.Value)), ending ?? now, now, Channel.Url, ending is null ? "observed" : "service"), true);
                if (start is not null) MergeBroadcast(state, new Broadcast { Id = "live:" + previous.Id + ":" + start.Value.ToString("O"), Title = previous.Title,
                    LiveSessionId = previous.Id.Length > 0 ? "id-" + previous.Id : null, StartedAt = start, EndedAt = ending, Seconds = duration ?? 0,
                    ImageUrl = previous.ImageUrl, Url = Channel.Url, TimingSource = duration > 0 ? "liveTiming" : "observedEnd" });
            }
            state.Live = live;
        }
        foreach (var (source, old, current) in new[] { ("치지직", state.Followers, followers), ("YouTube", state.YouTubeSubscribers, subscribers) })
            if (state.MediaBaseline && old is not null && current is not null && Milestone(current.Value) > Milestone(old.Value))
            {
                var milestone = Milestone(current.Value);
                QueueNotification(state, new Notification($"milestone:{source}:{milestone}", source, "달성", $"{(source == "치지직" ? "팔로워" : "구독자")} {milestone:N0}명 달성 · {Channel.DateText(now)} 확인", now, now, TimeBasis: "observed"), true);
            }
        state.Followers = followers ?? state.Followers;
        state.YouTubeSubscribers = subscribers ?? state.YouTubeSubscribers;
        state.MediaBaseline = true;
    }

    public static void CalendarMilestones(AppState state, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Channel.Korea).DateTime);
        var days = today.DayNumber - Channel.Debut.DayNumber;
        if (days > 0 && days % 100 == 0)
            QueueNotification(state, new Notification($"debut:{days}", "기념일", "데뷔", $"루파 데뷔 {days:N0}일", now, now), true);
        if (today.Month == 7 && today.Day == 23)
            QueueNotification(state, new Notification($"birthday:{today.Year}", "기념일", "생일", "오늘은 루파의 생일이에요!", now, now), true);
    }
}
