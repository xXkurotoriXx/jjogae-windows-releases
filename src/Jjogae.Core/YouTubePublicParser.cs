using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Jjogae.Core;

public sealed record YouTubePage(long? Subscribers, List<Media> Videos);
public sealed record YouTubePublicResult(long? Subscribers, Media? LatestVideo, List<Media> Videos, List<string> Errors);

/// <summary>Reads only the public channel header and selected Videos tab; never executes page scripts.</summary>
public static class YouTubePublicParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static YouTubePage ParsePage(string html, string expectedChannelId)
    {
        if (html.Length > 16 * 1024 * 1024) throw new InvalidDataException("YouTube 응답이 너무 큽니다.");
        var marker = Regex.Match(html, "(?:var\\s+)?ytInitialData\\s*=\\s*|window\\[\"ytInitialData\"\\]\\s*=\\s*", RegexOptions.CultureInvariant, RegexTimeout);
        if (!marker.Success) throw new InvalidDataException("YouTube 채널 정보를 읽지 못했습니다.");
        var start = marker.Index + marker.Length;
        if (start >= html.Length || html[start] != '{') throw new InvalidDataException("YouTube 채널 응답 형식을 확인해 주세요.");
        var bytes = Encoding.UTF8.GetBytes(html[start..]);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 128 });
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (J.String(J.At(root, "metadata", "channelMetadataRenderer"), "externalId") != expectedChannelId)
            throw new InvalidDataException("YouTube 채널 ID가 일치하지 않습니다.");

        long? subscribers = null;
        var header = J.At(root, "header");
        var legacy = J.At(header, "c4TabbedHeaderRenderer", "subscriberCountText");
        subscribers = ParseSubscriberText(Text(legacy));
        // Restrict the search to header metadata. Descriptions, recommendations and video views are unrelated counts.
        var metadata = J.At(header, "pageHeaderRenderer", "content", "pageHeaderViewModel", "metadata");
        foreach (var text in TextValues(metadata))
            if (ParseSubscriberText(text) is { } value) { subscribers = value; break; }

        var tabs = J.Items(root, "contents", "twoColumnBrowseResultsRenderer", "tabs");
        var selected = tabs.Select(t => J.At(t, "tabRenderer")).FirstOrDefault(t => J.Bool(J.At(t, "selected")));
        var videos = new List<Media>();
        foreach (var item in J.Items(selected, "content", "richGridRenderer", "contents"))
        {
            var content = J.At(item, "richItemRenderer", "content");
            if (ParseTile(content) is { } video) videos.Add(video);
        }
        // Older channel layouts expose a gridRenderer below the selected tab's item section.
        foreach (var section in J.Items(selected, "content", "sectionListRenderer", "contents"))
        foreach (var item in J.Items(section, "itemSectionRenderer", "contents"))
        foreach (var tile in J.Items(item, "gridRenderer", "items"))
            if (ParseTile(tile) is { } video) videos.Add(video);
        return new YouTubePage(subscribers, videos.DistinctBy(v => v.Id).ToList());
    }

    public static long? ParseSubscriberText(string raw)
    {
        raw = raw.Trim();
        foreach (var pattern in new[] {
            @"^구독자\s*([\d,]+(?:\.\d+)?)\s*(천|만|억|[KMB])?\s*명?$",
            @"^([\d,]+(?:\.\d+)?)\s*([KMB]|thousand|million|billion)?\s+subscribers?$",
            @"^subscribers?\s+([\d,]+(?:\.\d+)?)\s*([KMB])?$" })
        {
            var match = Regex.Match(raw, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
            if (!match.Success || !decimal.TryParse(match.Groups[1].Value.Replace(",", ""), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) continue;
            var factor = match.Groups[2].Value.ToLowerInvariant() switch {
                "천" or "k" or "thousand" => 1000m, "만" => 10000m, "억" => 100000000m,
                "m" or "million" => 1000000m, "b" or "billion" => 1000000000m, _ => 1m };
            if (number < 0 || number > 1_000_000_000m / factor) return null;
            return (long)decimal.Round(number * factor, 0, MidpointRounding.AwayFromZero);
        }
        return null;
    }

    public static List<Media> ParseFeed(string xml, string expectedChannelId)
    {
        var feed = XDocument.Parse(xml);
        XNamespace atom = "http://www.w3.org/2005/Atom", yt = "http://www.youtube.com/xml/schemas/2015";
        if (feed.Root?.Element(yt + "channelId")?.Value != expectedChannelId)
            throw new InvalidDataException("YouTube 영상 목록의 채널 ID가 일치하지 않습니다.");
        var videos = new List<Media>();
        foreach (var entry in feed.Descendants(atom + "entry"))
        {
            var id = entry.Element(yt + "videoId")?.Value ?? "";
            var title = entry.Element(atom + "title")?.Value ?? "";
            var channel = entry.Element(yt + "channelId")?.Value;
            if (channel != expectedChannelId) continue;
            var date = DateTimeOffset.TryParse(entry.Element(atom + "published")?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : (DateTimeOffset?)null;
            if (Video(id, title, date) is { } video) videos.Add(video);
        }
        return videos.DistinctBy(v => v.Id).OrderByDescending(v => v.PublishedAt).ToList();
    }

    public static YouTubePublicResult Combine(YouTubePage? page, List<Media> feed, List<string> errors)
    {
        var dated = feed.DistinctBy(v => v.Id).ToDictionary(v => v.Id);
        var pageVideos = page?.Videos.Take(1).Select(v => dated.TryGetValue(v.Id, out var known) ? v with { PublishedAt = known.PublishedAt } : v).ToList() ?? [];
        var videos = pageVideos.Concat(feed).DistinctBy(v => v.Id).ToList();
        // The Videos tab remains authoritative for the home card; Shorts in the feed do not displace it.
        var latest = pageVideos.FirstOrDefault() ?? feed.FirstOrDefault();
        return new YouTubePublicResult(page?.Subscribers, latest, videos, errors);
    }

    private static Media? ParseTile(JsonElement content)
    {
        var lockup = J.At(content, "lockupViewModel");
        if (J.String(lockup, "contentType") == "LOCKUP_CONTENT_TYPE_VIDEO")
            return Video(J.String(lockup, "contentId"), J.String(J.At(lockup, "metadata", "lockupMetadataViewModel", "title"), "content"));
        foreach (var key in new[] { "videoRenderer", "gridVideoRenderer" })
        {
            var legacy = J.At(content, key);
            if (Video(J.String(legacy, "videoId"), Text(J.At(legacy, "title"))) is { } video) return video;
        }
        return null;
    }

    private static Media? Video(string id, string title, DateTimeOffset? date = null)
    {
        title = title.Trim();
        if (!Regex.IsMatch(id, @"^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant, RegexTimeout) || title.Length == 0) return null;
        return new Media(id, "YouTube", "새 영상", title[..Math.Min(300, title.Length)], $"https://www.youtube.com/watch?v={id}", $"https://i.ytimg.com/vi/{id}/mqdefault.jpg", date);
    }

    private static string Text(JsonElement value)
    {
        var simple = J.String(value, "simpleText", "content");
        return simple.Length > 0 ? simple : string.Concat(J.Items(value, "runs").Select(r => J.String(r, "text")));
    }
    private static IEnumerable<string> TextValues(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if ((property.Name is "content" or "simpleText" or "text") && property.Value.ValueKind == JsonValueKind.String) yield return property.Value.GetString()!;
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    foreach (var text in TextValues(property.Value)) yield return text;
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) foreach (var text in TextValues(item)) yield return text;
    }
}
