using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jjogae.Core;

public sealed record ChatBadge(string Id, string Kind, string ImageUrl, string Title, string Detail);
public sealed record ChzzkChatProfile(string UserId, string ChannelId, IReadOnlyList<ChatBadge> Badges, int? SubscriptionMonths,
    string SubscriptionTier, int? ContinuousDonationDays, DateTimeOffset? FollowedAt, DateTimeOffset CheckedAt, string RefreshError = "")
{
    public bool BelongsTo(Account? account) => account is not null && account.UserId == UserId && ChannelId == Channel.Id;
}

public static class ChatProfileParser
{
    public static bool ValidUserId(string? value) => value is not null && Regex.IsMatch(value, "\\A[0-9a-f]{32}\\z");
    public static bool ValidChatId(string? value) => value is not null && Regex.IsMatch(value, "\\A[A-Za-z0-9_-]{1,64}\\z");
    public static string ProfileUrl(string chatId, string userId)
    {
        if (!ValidChatId(chatId) || !ValidUserId(userId)) throw new InvalidDataException("채팅 프로필의 계정을 확인하지 못했습니다.");
        return $"https://comm-api.game.naver.com/nng_main/v1/chats/{chatId}/users/{userId}/profile-card?chatType=STREAMING";
    }
    public static bool SafeImage(string value) => value.Length <= 2048 && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Host is "ssl.pstatic.net" or "nng-phinf.pstatic.net";
    private static int? Integer(JsonElement value, int maximum) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
        && double.IsFinite(number) && number == Math.Truncate(number) && number >= 0 && number <= maximum ? (int)number : null;
    private static string Text(JsonElement value, int limit) => value.ValueKind == JsonValueKind.String
        ? new string(value.GetString()!.Where(x => !char.IsControl(x)).Take(limit).ToArray()).Trim() : "";
    public static ChzzkChatProfile Parse(JsonElement root, string userId, DateTimeOffset now)
    {
        if (!ValidUserId(userId)) throw new InvalidDataException("채팅 프로필의 계정을 확인하지 못했습니다.");
        var content = J.At(root, "content");
        if (Integer(J.At(root, "code"), 999) != 200 || content.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("치지직 채팅 프로필 형식이 변경되었습니다.");
        if (Text(J.At(content, "userIdHash"), 100) != userId) throw new InvalidDataException("채팅 프로필의 계정이 일치하지 않습니다.");
        var modern = J.At(content, "viewerBadges"); var legacy = J.At(content, "activityBadges"); var streaming = J.At(content, "streamingProperty");
        if (modern.ValueKind != JsonValueKind.Array && legacy.ValueKind != JsonValueKind.Array && streaming.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("치지직 채팅 프로필 형식이 변경되었습니다.");
        var subscription = J.At(streaming, "subscription"); var months = Integer(J.At(subscription, "accumulativeMonth"), 1200);
        var tier = Text(J.At(subscription, "tierName"), 100); var badges = new List<ChatBadge>();
        void Append(JsonElement badge, string kind, string id, string fallback, string detail = "")
        {
            var image = Text(J.At(badge, "imageUrl"), 2049);
            if (badges.Count >= 16 || !SafeImage(image) || badges.Any(x => x.Id == id || x.ImageUrl == image)) return;
            var title = Text(J.At(badge, "title"), 100); var description = Text(J.At(badge, "description"), 240);
            badges.Add(new(id, kind, image, title.Length == 0 ? fallback : title, description.Length == 0 ? detail : description));
        }
        Append(J.At(content, "badge"), "role", "role", "역할 배지");
        Append(J.At(streaming, "realTimeDonationRanking", "badge"), "donationRanking", "donation-ranking", "후원 랭킹");
        Append(J.At(subscription, "badge"), "subscription", "subscription", "구독 배지", string.Join(" · ", new[] { tier, months is { } m ? $"{m}개월 구독 중" : "" }.Where(x => x.Length > 0)));
        if (modern.ValueKind == JsonValueKind.Array)
        {
            var entries = modern.EnumerateArray().Take(64).ToArray();
            var hasActivation = entries.Any(x => J.At(x, "activatedV2").ValueKind != JsonValueKind.Undefined);
            foreach (var entry in entries.Select((x, i) => (Item: x, Index: i)).Where(x => !hasActivation || J.At(x.Item, "activatedV2").ValueKind == JsonValueKind.True)
                .OrderByDescending(x => Text(J.At(x.Item, "badge", "scope"), 20) == "CHANNEL").ThenBy(x => Integer(J.At(x.Item, "order"), 10_000) ?? int.MaxValue).ThenBy(x => x.Index))
            {
                var badge = J.At(entry.Item, "badge"); var id = Text(J.At(badge, "badgeId"), 120);
                Append(badge, "activity", "activity:" + (id.Length > 0 ? id : entry.Index.ToString(CultureInfo.InvariantCulture)), "활동 배지");
            }
        }
        else if (modern.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            foreach (var (badge, index) in J.Items(content, "activityBadges").Take(64).Select((x, i) => (x, i)))
                if (J.At(badge, "activated").ValueKind == JsonValueKind.True)
                { var id = Text(J.At(badge, "badgeId"), 120); Append(badge, "activity", "activity:" + (id.Length > 0 ? id : index.ToString(CultureInfo.InvariantCulture)), "활동 배지"); }
        }
        DateTimeOffset? followed = null;
        var date = Text(J.At(streaming, "following", "followDate"), 30);
        if (DateTime.TryParseExact(date, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) followed = new DateTimeOffset(parsed, TimeSpan.FromHours(9));
        return new(userId, Channel.Id, badges, months, tier, Integer(J.At(streaming, "donationActivity", "continuousDonationDays"), 100_000), followed, now);
    }
}
