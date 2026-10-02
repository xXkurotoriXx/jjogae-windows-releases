using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jjogae.Core;

public enum ReplayAvailability { Unknown, Available, Unavailable }

public static class BroadcastAvailability
{
    private static TimeSpan CheckInterval(AppState state) => TimeSpan.FromSeconds(state.Settings.RefreshSeconds);
    // Include a grace period for an app that was offline during the first two days.
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);
    public static string? Url(string id) => Regex.IsMatch(id, @"\A[0-9]{1,100}\z")
        ? $"https://api.chzzk.naver.com/service/v3/videos/{id}" : null;

    public static ReplayAvailability Parse(JsonElement root, int status, string id)
    {
        if (status == 400 && J.Number(J.At(root, "code")) == 400
            && J.String(root, "message") == "이용할 수 없는 동영상입니다."
            && J.At(root, "content").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ReplayAvailability.Unavailable;
        if (status == 404 && J.Number(J.At(root, "code")) == 404
            && J.String(root, "message") == "동영상 정보가 존재하지 않습니다."
            && J.At(root, "content").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ReplayAvailability.Unavailable;
        var content = J.At(root, "content");
        return status == 200 && J.Number(J.At(root, "code")) == 200
            && J.String(content, "videoNo") == id && J.String(content, "videoType") == "REPLAY"
            && J.String(J.At(content, "channel"), "channelId") == Channel.Id
            ? ReplayAvailability.Available : ReplayAvailability.Unknown;
    }

    public static string[] Candidates(AppState state, DateTimeOffset now) => state.Broadcasts
        .Where(x => x.VideoId is { } id && Url(id) is not null && x.PublishedAt is { } published
            && published >= now - RecentWindow && published <= now.AddMinutes(2)
            && (x.AvailabilityCheckedAt is null || now - x.AvailabilityCheckedAt >= CheckInterval(state)))
        .OrderBy(x => x.AvailabilityCheckedAt).ThenByDescending(x => x.PublishedAt)
        .Select(x => x.VideoId!).Distinct().Take(3).ToArray();

    public static List<Broadcast> Observe(AppState state, string id, ReplayAvailability availability, DateTimeOffset now)
    {
        var records = state.Broadcasts.Where(x => x.VideoId == id).ToArray();
        if (records.Length == 0) return [];
        var interval = CheckInterval(state);
        // Repeated calls, clock rollback, and future evidence cannot confirm deletion.
        if (records.Any(x => x.AvailabilityCheckedAt is { } check && now - check < interval)) return [];
        var confirmed = availability == ReplayAvailability.Unavailable
            && records.Any(x => x.ReplayMissingSince is { } since && now - since >= interval && now - since <= RecentWindow);
        foreach (var record in records)
        {
            record.AvailabilityCheckedAt = now;
            record.ReplayMissingSince = availability != ReplayAvailability.Unavailable ? null
                : record.ReplayMissingSince is { } first && now >= first && now - first <= RecentWindow ? first : now;
        }
        if (!confirmed) return [];

        state.DeletedReplayIds.Add(id);
        var removed = state.Broadcasts.Where(x => x.VideoId == id || x.VideoId is null
            && x.Id.StartsWith("live:", StringComparison.Ordinal) && records.Any(r => BroadcastLogic.Same(x, r))
            && !state.Broadcasts.Any(other => other.VideoId is { } otherId && otherId != id && BroadcastLogic.Same(x, other))).ToList();
        foreach (var record in removed)
            if (record.LiveSessionId is { } session && !state.Broadcasts.Any(x => x.VideoId is { } otherId && otherId != id && x.LiveSessionId == session))
                state.DeletedBroadcastSessions.Add(session);
        var removedIds = removed.Select(x => x.Id).ToHashSet();
        state.Broadcasts.RemoveAll(x => removedIds.Contains(x.Id));
        state.Media.RemoveAll(x => x.Source == "치지직" && x.Kind == "다시보기" && x.Id == id);
        state.Pending.RemoveAll(x => x.Source == "치지직" && (x.Id == "media:치지직:" + id
            || x.Kind is "방송 시작" or "방송 종료" && records.Any(r => r.StartedAt is { } start
                && !state.Broadcasts.Any(other => BroadcastLogic.Same(other, r))
                && x.Id.EndsWith(":" + start.ToString("O"), StringComparison.Ordinal))));
        return removed;
    }
}
