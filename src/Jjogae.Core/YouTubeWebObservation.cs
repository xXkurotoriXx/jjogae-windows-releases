using System.Text.Json;

namespace Jjogae.Core;

public sealed record YouTubeWebObservation(bool? Subscribed, bool? MembershipActive, int? MembershipMonths,
    string? Tier, string? NextBillingLabel, DateTimeOffset CheckedAt, long? Subscribers = null)
{
    public static YouTubeWebObservation? Decode(JsonElement fields, DateTimeOffset now)
    {
        if (J.String(fields, "channelID") != Channel.YouTubeId || J.At(fields, "loggedIn").ValueKind != JsonValueKind.True) return null;
        bool? Flag(string key) => J.At(fields, key).ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
        string? Label(string key, int limit) { var text = J.String(fields, key).Trim(); return text.Length > 0 && text.Length <= limit ? text : null; }
        var subscribed = Flag("subscribed"); var active = Flag("active");
        // Login is useful even while channel controls are still loading.
        var monthField = J.At(fields, "months");
        int? months = monthField.ValueKind == JsonValueKind.Number && monthField.TryGetInt32(out var count) && count is >= 0 and <= 1200 ? count : null;
        return new(subscribed, active, active == false ? null : months, active == false ? null : Label("tier", 100), active == false ? null : Label("billing", 60), now,
            YouTubePublicParser.ParseSubscriberText(Label("subscriberText", 80) ?? ""));
    }
    public YouTubeWebObservation Merge(YouTubeWebObservation next) => new(next.Subscribed ?? Subscribed,
        next.MembershipActive ?? MembershipActive, next.MembershipActive == false ? null : next.MembershipMonths ?? MembershipMonths,
        next.MembershipActive == false ? null : next.Tier ?? Tier, next.MembershipActive == false ? null : next.NextBillingLabel ?? NextBillingLabel, next.CheckedAt, next.Subscribers ?? Subscribers);
}

public static class YouTubeWebPolicy
{
    public const string Membership = Channel.YouTubeUrl + "/membership";
    public const string PaidMemberships = "https://www.youtube.com/paid_memberships";
    public static bool Allows(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0
        && (new[] { "youtube.com", "www.youtube.com", "m.youtube.com", "accounts.youtube.com", "accounts.google.com", "accounts.google.co.kr", "accounts.google.co.in", "www.google.com", "consent.google.com", "consent.youtube.com" }.Contains(uri.Host)
            || (uri.Host is "www.google.co.kr" or "www.google.co.in") && uri.AbsolutePath.StartsWith("/accounts/", StringComparison.Ordinal));
    public static bool CanObserve(string address)
    {
        if (!Allows(address) || new Uri(address).Host != "www.youtube.com") return false;
        var path = new Uri(address).AbsolutePath.TrimEnd('/');
        return path is "" or "/paid_memberships" || path.StartsWith("/channel/", StringComparison.Ordinal) || path.StartsWith("/@", StringComparison.Ordinal);
    }
}
