using System.Globalization;
using System.Text.Json.Serialization;

namespace Jjogae.Core;

public static class Channel
{
    public const string Name = "아홀로 루파";
    public const string Id = "3e948667805e7627459a599018d05853";
    public const string YouTubeId = "UC9gVXsUQNKvj-sn_SAdDi9g";
    public const string CafeId = "31522940";
    public const string Url = "https://chzzk.naver.com/" + Id;
    public const string CafeUrl = "https://cafe.naver.com/aholorupacafe";
    public const string YouTubeUrl = "https://www.youtube.com/channel/" + YouTubeId;
    public const string YouTubeReplayUrl = "https://www.youtube.com/@%EB%A3%A8%ED%8C%8C%EC%9D%98_%EB%8B%A4%EC%8B%9C%EB%B3%B4%EA%B8%B0";
    public static readonly DateOnly Debut = new(2025, 6, 15);
    public static readonly TimeZoneInfo Korea = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
    public static DateTimeOffset KoreanNow => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Korea);
    public static DateOnly Today => DateOnly.FromDateTime(KoreanNow.DateTime);
    public static string DateText(DateTimeOffset? value) => value is null ? "날짜 미확인" : TimeZoneInfo.ConvertTime(value.Value, Korea).ToString("yyyy.MM.dd HH:mm", CultureInfo.InvariantCulture);
    public static string Elapsed(DateOnly? value) => value is null ? "미입력" : $"D+{Math.Max(0, Today.DayNumber - value.Value.DayNumber):N0}일";
    public static string Duration(double seconds)
    {
        var value = double.IsFinite(seconds) ? (long)Math.Clamp(seconds, 0, 3_155_760_000) : 0;
        return $"{value / 3600}시간 {value % 3600 / 60}분";
    }
    public static int BirthdayRemaining(DateOnly today)
    {
        var next = new DateOnly(today.Year, 7, 23);
        if (next < today) next = next.AddYears(1);
        return next.DayNumber - today.DayNumber;
    }
}

public sealed record Preferences
{
    public int RefreshSeconds { get; set; } = 60;
    public bool Notifications { get; set; }
    public string Theme { get; set; } = "system";
    public double Transparency { get; set; } = .6;
    // Keep the existing backup field while displaying the image's actual opacity in settings.
    [JsonIgnore] public double BackgroundOpacity
    {
        get => 1 - (double.IsFinite(Transparency) ? Math.Clamp(Transparency, 0, 1) : .6);
        set => Transparency = 1 - (double.IsFinite(value) ? Math.Clamp(value, 0, 1) : .4);
    }
    public int PageSize { get; set; } = 10;
    public DateOnly? YouTubeSubscribedOn { get; set; }
    public DateOnly? YouTubeMemberSince { get; set; }
    public bool YouTubeMemberActive { get; set; }
    public bool YouTubeWebEnabled { get; set; }
    public bool YouTubeResetPending { get; set; }
    public bool YouTubeMembershipHidden { get; set; }
    public int YouTubeAdditionalMonths { get; set; }
    public bool SidebarVisible { get; set; }
    public bool SaveThumbnails { get; set; } = true;
    public bool RelationshipDurationsVisible { get; set; } = true;
    public string ExternalBrowser { get; set; } = "system";
    public void Normalize()
    {
        if (!new[] { 60, 300, 600, 3600 }.Contains(RefreshSeconds)) RefreshSeconds = 60;
        if (!new[] { 10, 20, 50, 100 }.Contains(PageSize)) PageSize = 10;
        if (!new[] { "system", "light", "dark" }.Contains(Theme)) Theme = "system";
        Transparency = double.IsFinite(Transparency) ? Math.Clamp(Transparency, 0, 1) : .6;
        if (YouTubeSubscribedOn > Channel.Today) YouTubeSubscribedOn = null;
        if (YouTubeMemberSince > Channel.Today) YouTubeMemberSince = null;
        YouTubeAdditionalMonths = Math.Clamp(YouTubeAdditionalMonths, 0, 1200);
        if (!new[] { "system", "edge", "chrome", "firefox" }.Contains(ExternalBrowser)) ExternalBrowser = "system";
    }
}

public sealed record Account(string Nickname, long? Power, string Subscription, DateOnly? FollowedOn, string Renewal, string? UserId = null, bool IsSubscribed = false);
public sealed record Media(string Id, string Source, string Kind, string Title, string Url, string ImageUrl, DateTimeOffset? PublishedAt);
public sealed record LiveObservation(bool IsLive, string Id, string Title, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt, string ImageUrl);
public sealed record Cheese(string Id, string Streamer, long Amount, string Message, string Kind, DateTimeOffset At);
public sealed record CafePost(string Id, string Title, string Author, string Menu, bool Notice, DateTimeOffset At, string Url)
{
    [JsonIgnore] public bool Rupa => string.Concat(Author.Where(c => !char.IsWhiteSpace(c))) == "아홀로루파";
    [JsonIgnore] public bool Protected => Notice || Rupa;
}
public sealed record Notification(string Id, string Source, string Kind, string Title, DateTimeOffset At, DateTimeOffset DetectedAt, string Url = "", string TimeBasis = "legacy");
public sealed record Broadcast
{
    public string Id { get; init; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public double Seconds { get; set; }
    public string Url { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string TimingSource { get; set; } = "vodDuration";
    public string? LiveSessionId { get; set; }
    public string? VideoId { get; set; }
    public double? VodSeconds { get; set; }
    public DateTimeOffset? DetailCheckedAt { get; set; }
    public DateTimeOffset? AvailabilityCheckedAt { get; set; }
    public DateTimeOffset? ReplayMissingSince { get; set; }
    public BroadcastTimingOverride? ManualTiming { get; set; }
    public string? ThumbnailFilename { get; set; }
    public bool ThumbnailIsCustom { get; set; }
    [JsonIgnore] public DateTimeOffset? EffectiveStartedAt => ManualTiming?.StartedAt ?? StartedAt;
    [JsonIgnore] public DateTimeOffset? EffectiveEndedAt => ManualTiming?.EndedAt ?? EndedAt;
    [JsonIgnore] public double EffectiveSeconds => ManualTiming?.Seconds ?? Seconds;
    [JsonIgnore] public DateTimeOffset Date => EffectiveStartedAt ?? PublishedAt ?? EffectiveEndedAt ?? DateTimeOffset.MinValue;
}

public sealed class AppState
{
    public int SchemaVersion { get; set; } = 1;
    public Preferences Settings { get; set; } = new();
    public List<Cheese> Cheese { get; set; } = [];
    public List<CafePost> Cafe { get; set; } = [];
    public HashSet<string> ReadCafe { get; set; } = [];
    public HashSet<string> SeenCafe { get; set; } = [];
    public bool CafeBaseline { get; set; }
    public DateTimeOffset? CafeCheckedAt { get; set; }
    public List<Broadcast> Broadcasts { get; set; } = [];
    public HashSet<string> DeletedReplayIds { get; set; } = [];
    public HashSet<string> DeletedBroadcastSessions { get; set; } = [];
    public HashSet<string> NotificationKeys { get; set; } = [];
    [JsonIgnore] public List<Notification> Pending { get; set; } = [];
    public List<Media> Media { get; set; } = [];
    public LiveObservation? Live { get; set; }
    public long? Followers { get; set; }
    public long? YouTubeSubscribers { get; set; }
    public DateTimeOffset? YouTubeCheckedAt { get; set; }
    public string? YouTubeLatestVideoId { get; set; }
    public YouTubeWebObservation? YouTubeWeb { get; set; }
    public bool MediaBaseline { get; set; }
    public DateTimeOffset? LastUpdatedAt { get; set; }
    [JsonIgnore] public Account? Account { get; set; }
    [JsonIgnore] public ChzzkChatProfile? ChatProfile { get; set; }
    public void Normalize()
    {
        if (SchemaVersion != 1) throw new InvalidDataException("지원하지 않는 백업 버전입니다.");
        Settings ??= new(); Settings.Normalize();
        // Earlier builds stored a manually entered count here. Do not present it as an automatic observation.
        if (YouTubeCheckedAt is null || YouTubeSubscribers is < 0 or > 1_000_000_000) YouTubeSubscribers = null;
        Cheese ??= []; Cafe ??= []; ReadCafe ??= []; SeenCafe ??= [];
        Broadcasts ??= []; NotificationKeys ??= []; Pending ??= []; Media ??= [];
        Broadcasts = Broadcasts.DistinctBy(x => x.Id).Select(BroadcastLogic.Normalize).OrderByDescending(x => x.Date).ToList();
        DeletedReplayIds ??= []; DeletedBroadcastSessions ??= [];
        DeletedReplayIds.RemoveWhere(x => x is null || BroadcastAvailability.Url(x) is null);
        DeletedBroadcastSessions.RemoveWhere(x => string.IsNullOrEmpty(x) || x.Length > 103);
        Broadcasts.RemoveAll(x => x.VideoId is { } id && DeletedReplayIds.Contains(id)
            || x.VideoId is null && x.Id.StartsWith("live:", StringComparison.Ordinal) && x.LiveSessionId is { } session && DeletedBroadcastSessions.Contains(session));
        Media.RemoveAll(x => x.Source == "치지직" && x.Kind == "다시보기" && DeletedReplayIds.Contains(x.Id));
        NotificationKeys.RemoveWhere(x => x is null || x.Length != 64 || !x.All(Uri.IsHexDigit));
        Cheese = Cheese.Where(x => x.Amount >= 0).DistinctBy(x => x.Id).OrderByDescending(x => x.At).ToList();
        Cafe = Cafe.DistinctBy(x => x.Id).OrderByDescending(x => x.At).ToList();
    }
}
