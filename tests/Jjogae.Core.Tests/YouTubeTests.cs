using System.Text.Json;
using System.Text.Json.Nodes;
using Jjogae.Core;

static class YouTubeTests
{
    public static async Task RunNetwork(Action<bool, string> check)
    {
        var html = """
        var ytInitialData = {"metadata":{"channelMetadataRenderer":{"externalId":"UC9gVXsUQNKvj-sn_SAdDi9g"}},"header":{"c4TabbedHeaderRenderer":{"subscriberCountText":{"simpleText":"6.79K subscribers"}}},"contents":{"twoColumnBrowseResultsRenderer":{"tabs":[{"tabRenderer":{"selected":true,"content":{"richGridRenderer":{"contents":[{"richItemRenderer":{"content":{"videoRenderer":{"videoId":"abcdefghijk","title":{"simpleText":"Latest"}}}}}]}}}}]}}};
        """;
        var feed = $$"""
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:yt="http://www.youtube.com/xml/schemas/2015"><yt:channelId>{{Channel.YouTubeId}}</yt:channelId><entry><yt:channelId>{{Channel.YouTubeId}}</yt:channelId><yt:videoId>abcdefghijk</yt:videoId><title>Latest</title><published>2026-09-01T12:00:00Z</published></entry></feed>
        """;
        foreach (var pageTimeout in new[] { false, true })
        {
            var calls = new List<Uri>(); var cookieRequested = false;
            using var api = new ApiClient(new FixtureHandler(request =>
            {
                calls.Add(request.RequestUri!);
                check(!request.Headers.Contains("Cookie") && request.Headers.Authorization is null, "public request has no login credentials");
                var isPage = request.RequestUri!.AbsolutePath.EndsWith("/videos");
                if (isPage == pageTimeout) throw new TaskCanceledException("Simulated HTTP timeout");
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(isPage ? html : feed) };
            }));
            api.CookieHeader = _ => { cookieRequested = true; return Task.FromResult(""); };
            var result = await api.YouTubePublic();
            check(!cookieRequested && calls.Count == 2 && calls.All(u => u.Host == "www.youtube.com" && u.Scheme == "https"), "public sources only, no login lookup");
            check(result.LatestVideo?.Id == "abcdefghijk", "one source times out, other still supplies latest video");
            check(pageTimeout ? result.Subscribers is null && result.Errors.Count > 0 : result.Subscribers == 6790 && result.Errors.Count == 0, "timeout preserves independent channel and feed results");
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var canceledApi = new ApiClient(new FixtureHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)));
        var propagated = false;
        try { await canceledApi.YouTubePublic(canceled.Token); } catch (OperationCanceledException) { propagated = true; }
        check(propagated, "explicit cancellation is not swallowed as network failure");
    }

    private sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    public static void Run(Action<bool, string> check)
    {
        foreach (var (text, count) in new (string, long?)[] {
            ("구독자 6.79천명", 6790), ("구독자 6,790명", 6790), ("구독자 1.25만명", 12500),
            ("구독자 1.2억명", 120000000), ("6.79K subscribers", 6790), ("1.25M subscribers", 1250000),
            ("1B subscribers", 1000000000), ("123 subscribers", 123), ("1 subscriber", 1),
            ("Subscribers 10K", 10000), ("2 thousand subscribers", 2000), ("0 subscribers", 0),
            ("7.8K views", null), ("125 videos", null), ("구독자 비공개", null), ("", null),
            ("구독자 -10명", null), ("2B subscribers", null), ("900000000000000000000000000000 subscribers", null),
            ("description: 100K subscribers", null) })
            check(YouTubePublicParser.ParseSubscriberText(text) == count, "subscriber parser / " + text);

        var root = JsonNode.Parse("""
        {"metadata":{"channelMetadataRenderer":{"externalId":"UC9gVXsUQNKvj-sn_SAdDi9g","description":"999M subscribers"}},
         "header":{"pageHeaderRenderer":{"content":{"pageHeaderViewModel":{"metadata":{"contentMetadataViewModel":{"metadataRows":[{"metadataParts":[{"text":{"content":"구독자 6.79천명"}},{"text":{"content":"동영상 125개"}}]}]}}}}}},
         "contents":{"twoColumnBrowseResultsRenderer":{"tabs":[
           {"tabRenderer":{"selected":false,"content":{"richGridRenderer":{"contents":[{"richItemRenderer":{"content":{"videoRenderer":{"videoId":"unselected1","title":{"simpleText":"not selected"}}}}}]}}}},
           {"tabRenderer":{"selected":true,"content":{"richGridRenderer":{"contents":[
             {"richItemRenderer":{"content":{"lockupViewModel":{"contentType":"LOCKUP_CONTENT_TYPE_PLAYLIST","contentId":"playlist123","metadata":{"lockupMetadataViewModel":{"title":{"content":"Not a video"}}}}}}},
             {"richItemRenderer":{"content":{"lockupViewModel":{"contentType":"LOCKUP_CONTENT_TYPE_VIDEO","contentId":"abcdefghijk","metadata":{"lockupMetadataViewModel":{"title":{"content":"한국어 최신 영상"}}}}}}},
             {"richItemRenderer":{"content":{"videoRenderer":{"videoId":"lmnopqrstuv","title":{"runs":[{"text":"이전 "},{"text":"영상"}]}}}}},
             {"richItemRenderer":{"content":{"videoRenderer":{"videoId":"bad/id","title":{"simpleText":"invalid"}}}}}
           ]}}}}
         ]}}}
        """)!;
        string Html(JsonNode node, string marker = "var ytInitialData = ") => "<script>" + marker + node.ToJsonString() + "; thisIsNeverExecuted();</script>";
        var page = YouTubePublicParser.ParsePage(Html(root), Channel.YouTubeId);
        check(page.Subscribers == 6790, "modern channel header count");
        check(page.Videos.Select(v => v.Id).SequenceEqual(new[] { "abcdefghijk", "lmnopqrstuv" }), "selected videos only, no playlists or malformed IDs");
        check(page.Videos[0].Title == "한국어 최신 영상" && page.Videos[1].Title == "이전 영상", "modern and legacy titles");
        check(page.Videos.All(v => v.PublishedAt is null && ApiClient.SafeImage(v.ImageUrl)), "HTML does not invent publish dates");
        check(YouTubePublicParser.ParsePage(Html(root, "window[\"ytInitialData\"] = "), Channel.YouTubeId).Subscribers == 6790, "window initial data marker");
        var hidden = root.DeepClone(); hidden["header"] = new JsonObject();
        check(YouTubePublicParser.ParsePage(Html(hidden), Channel.YouTubeId).Subscribers is null, "description count is never subscriber count");
        var legacy = hidden.DeepClone();
        legacy["header"] = JsonNode.Parse("""{"c4TabbedHeaderRenderer":{"subscriberCountText":{"simpleText":"6.79K subscribers"}}}""");
        check(YouTubePublicParser.ParsePage(Html(legacy), Channel.YouTubeId).Subscribers == 6790, "legacy header count");
        void Rejected(Action action, string label)
        {
            var rejected = false; try { action(); } catch (Exception e) when (e is InvalidDataException or JsonException or System.Xml.XmlException) { rejected = true; }
            check(rejected, label);
        }
        Rejected(() => YouTubePublicParser.ParsePage(Html(root), "another-channel"), "wrong channel rejected");
        Rejected(() => YouTubePublicParser.ParsePage("<html>sign in</html>", Channel.YouTubeId), "missing page data rejected");
        Rejected(() => YouTubePublicParser.ParsePage("var ytInitialData = function(){}", Channel.YouTubeId), "script instead of JSON rejected");
        Rejected(() => YouTubePublicParser.ParsePage("var ytInitialData = {broken", Channel.YouTubeId), "malformed JSON rejected");

        var feedText = $$"""
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:yt="http://www.youtube.com/xml/schemas/2015">
          <yt:channelId>{{Channel.YouTubeId}}</yt:channelId>
          <entry><yt:channelId>{{Channel.YouTubeId}}</yt:channelId><yt:videoId>abcdefghijk</yt:videoId><title>Feed title</title><published>2026-09-01T12:00:00Z</published></entry>
          <entry><yt:channelId>{{Channel.YouTubeId}}</yt:channelId><yt:videoId>newshort123</yt:videoId><title>New Short</title><published>2026-09-02T12:00:00Z</published></entry>
          <entry><yt:channelId>another-channel</yt:channelId><yt:videoId>foreign1234</yt:videoId><title>Wrong channel</title></entry>
          <entry><yt:channelId>{{Channel.YouTubeId}}</yt:channelId><yt:videoId>bad</yt:videoId><title>Bad ID</title></entry>
        </feed>
        """;
        var feed = YouTubePublicParser.ParseFeed(feedText, Channel.YouTubeId);
        check(feed.Count == 2 && feed[0].Id == "newshort123", "feed validates entries and sorts exact timestamps");
        Rejected(() => YouTubePublicParser.ParseFeed(feedText, "other"), "feed wrong channel rejected");
        var combined = YouTubePublicParser.Combine(page, feed, []);
        check(combined.LatestVideo is { Id: "abcdefghijk", Title: "한국어 최신 영상", PublishedAt: not null }, "Videos tab latest enriched with feed timestamp");
        check(combined.Videos.Count == 2 && combined.Videos.All(v => v.Id != "lmnopqrstuv"), "no historical HTML notification flood");
        var fallback = YouTubePublicParser.Combine(page, [], []);
        check(fallback.Subscribers == 6790 && fallback.LatestVideo?.Id == "abcdefghijk", "RSS unavailable, HTML still works");
        check(YouTubePublicParser.Combine(null, feed, []).LatestVideo?.Id == "newshort123", "channel unavailable, feed fallback");

        var at = DateTimeOffset.Parse("2026-09-01T12:00:00Z");
        var state = new AppState { YouTubeSubscribers = 9999, YouTubeCheckedAt = at, MediaBaseline = true, Media = [combined.LatestVideo!] };
        state.Settings.Notifications = true;
        Policies.ObserveMedia(state, fallback.Videos, null, null, 10000, at);
        check(state.Media[0].PublishedAt == at, "previous publish date survives HTML fallback");
        check(state.YouTubeSubscribers == 10000 && state.Pending.Count(a => a.Kind == "달성") == 1, "automatic YouTube milestone");
        Policies.ObserveMedia(state, [], null, null, null, at.AddMinutes(1));
        check(state.YouTubeSubscribers == 10000 && state.Media.Count == 1, "failed refresh retains known count and video");
        var legacyState = JsonSerializer.Deserialize<AppState>("""{"schemaVersion":1,"youTubeSubscribers":12345,"settings":{"manualYouTubeSubscribers":12345,"youTubeSubscribedOn":"2026-01-01","youTubeMemberSince":"2026-02-01"},"readCafe":["123"]}""", StateStore.Json)!;
        legacyState.Normalize();
        check(legacyState.YouTubeSubscribers is null, "legacy manual count not represented as automatic");
        check(legacyState.Settings.YouTubeSubscribedOn == new DateOnly(2026, 1, 1) && legacyState.Settings.YouTubeMemberSince == new DateOnly(2026, 2, 1) && legacyState.ReadCafe.Contains("123"), "migration preserves personal dates and history");
        check(!JsonSerializer.Serialize(legacyState, StateStore.Json).Contains("manualYouTubeSubscribers"), "retired manual count removed from storage");
        var restored = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state, StateStore.Json), StateStore.Json)!; restored.Normalize();
        check(restored.YouTubeSubscribers == 10000 && restored.YouTubeCheckedAt == at, "automatic count persists with observation timestamp");
    }
}
