using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jjogae.Core;

public sealed class ApiClient : IDisposable
{
    private readonly HttpClient http;
    private string? currentChatId;
    public Func<Uri, Task<string>>? CookieHeader { get; set; }
    public ApiClient(HttpMessageHandler? transport = null)
    {
        http = new HttpClient(transport ?? new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(25) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0.0.0 Safari/537.36 JjogaeWindows/0.1");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ko-KR,ko;q=0.9");
    }

    private async Task<string> Text(string url, bool authenticated = false, CancellationToken token = default, int maximumBytes = 16 * 1024 * 1024)
    {
        var uri = new Uri(url);
        if (!new[] { "api.chzzk.naver.com", "comm-api.game.naver.com", "apis.naver.com", "www.youtube.com" }.Contains(uri.Host) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0)
            throw new InvalidOperationException("허용하지 않은 데이터 주소입니다.");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (authenticated && CookieHeader is not null)
        {
            var cookies = await CookieHeader(uri);
            if (!string.IsNullOrEmpty(cookies)) request.Headers.TryAddWithoutValidation("Cookie", cookies);
        }
        request.Headers.Referrer = new Uri(uri.Host == "apis.naver.com" ? Channel.CafeUrl : uri.Host == "www.youtube.com" ? Channel.YouTubeUrl : Channel.Url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new InvalidOperationException("로그인이 필요하거나 해당 자료에 접근할 권한이 없습니다.");
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(maximumBytes, token);
        return await response.Content.ReadAsStringAsync(token);
    }

    private async Task<JsonElement> Json(string url, bool auth = false, CancellationToken token = default)
    {
        using var document = JsonDocument.Parse(await Text(url, auth, token));
        return document.RootElement.Clone();
    }

    public async Task<string?> AccountName(CancellationToken token = default)
    {
        return (await AccountIdentity(token))?.Nickname;
    }

    public async Task<Account?> AccountIdentity(CancellationToken token = default)
    {
        var content = J.At(await Json("https://comm-api.game.naver.com/nng_main/v1/user/getUserStatus", true, token), "content");
        if (!J.Bool(J.At(content, "loggedIn"))) return null;
        var userId = J.String(content, "userIdHash"); var nickname = J.String(content, "nickname").Trim();
        return new Account(nickname.Length == 0 ? "로그인 계정" : new string(nickname.Take(100).ToArray()), null, "확인 중", null, "", ChatProfileParser.ValidUserId(userId) ? userId : null);
    }

    public async Task<ChzzkChatProfile> ChatProfile(Account account, CancellationToken token = default)
    {
        if (!ChatProfileParser.ValidUserId(account.UserId)) throw new InvalidDataException("채팅 프로필의 계정을 확인하지 못했습니다.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        if (currentChatId is null)
        {
            using var status = JsonDocument.Parse(await Text($"https://api.chzzk.naver.com/polling/v2/channels/{Channel.Id}/live-status", token: timeout.Token, maximumBytes: 256 * 1024));
            var id = J.String(J.At(status.RootElement, "content"), "chatChannelId"); currentChatId = ChatProfileParser.ValidChatId(id) ? id : null;
        }
        if (currentChatId is null) throw new InvalidDataException("채팅 배지를 일시적으로 확인하지 못했습니다.");
        using var profile = JsonDocument.Parse(await Text(ChatProfileParser.ProfileUrl(currentChatId, account.UserId!), true, timeout.Token, 256 * 1024));
        return ChatProfileParser.Parse(profile.RootElement, account.UserId!, DateTimeOffset.UtcNow);
    }

    public async Task<Account> AccountDetails(string nickname, CancellationToken token = default)
    {
        long? power = null; var tier = "미확인"; var renewal = ""; var subscribed = false; DateOnly? follow = null;
        var failures = 0;
        try
        {
            var balances = await Json("https://api.chzzk.naver.com/service/v1/log-power/balances", true, token);
            var match = J.Items(balances, "content", "data").FirstOrDefault(x => J.String(x, "channelId") == Channel.Id);
            power = J.Number(J.At(match, "amount"));
        }
        catch (Exception error) when (error is not OperationCanceledException) { failures++; }
        try
        {
            var root = await Json($"https://api.chzzk.naver.com/commercial/v1/subscribe/channels/{Channel.Id}", true, token);
            var subscription = ChzzkSubscription.Parse(root);
            tier = subscription.Tier; renewal = subscription.Renewal; subscribed = subscription.Active;
        }
        catch (Exception error) when (error is not OperationCanceledException) { failures++; }
        try
        {
            var root = await Json($"https://api.chzzk.naver.com/service/v1.1/channels/{Channel.Id}/my-info", true, token);
            var relationship = J.At(root, "content", "following");
            var date = J.Date(J.At(relationship, "followDate"));
            if (J.Bool(J.At(relationship, "following")) && date is not null)
                follow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(date.Value, Channel.Korea).DateTime);
        }
        catch (Exception error) when (error is not OperationCanceledException) { failures++; }
        if (failures == 3) throw new InvalidOperationException("로그인은 확인했지만 치지직 정보를 불러오지 못했습니다. 다시 확인해 주세요.");
        return new Account(nickname, power, tier, follow, renewal, IsSubscribed: subscribed);
    }

    public async Task<(LiveObservation? Live, long? Followers, YouTubePublicResult YouTube, List<Media> Media, List<Broadcast> Broadcasts, List<string> Errors)> Public(CancellationToken token = default)
    {
        var youtubeTask = YouTubePublic(token);
        var clipsTask = ReadClips();
        LiveObservation? live = null; long? followers = null; var media = new List<Media>(); var broadcasts = new List<Broadcast>(); var errors = new List<string>();
        try
        {
            var root = await Json($"https://api.chzzk.naver.com/polling/v2/channels/{Channel.Id}/live-status", token: token);
            var c = J.At(root, "content"); var status = J.String(c, "status", "liveStatus");
            var chatId = J.String(c, "chatChannelId"); currentChatId = ChatProfileParser.ValidChatId(chatId) ? chatId : null;
            if (status is not ("OPEN" or "CLOSE" or "CLOSED")) throw new InvalidDataException("방송 상태를 확인하지 못했습니다.");
            live = new LiveObservation(status == "OPEN", J.String(c, "liveId", "liveNo"), J.String(c, "liveTitle", "title"),
                J.Date(J.First(c, "openDate", "liveOpenDate", "startedAt")), J.Date(J.First(c, "closeDate", "liveCloseDate", "endedAt")), J.Image(c, "liveImageUrl", "thumbnailImageUrl"));
        }
        catch (Exception e) when (e is not OperationCanceledException) { errors.Add("방송: " + e.Message); }
        try { followers = J.Number(J.At(await Json($"https://api.chzzk.naver.com/service/v1/channels/{Channel.Id}", token: token), "content", "followerCount")); }
        catch (Exception e) when (e is not OperationCanceledException) { errors.Add("팔로워: " + e.Message); }
        try
        {
            var root = await Json($"https://api.chzzk.naver.com/service/v1/channels/{Channel.Id}/videos?sortType=LATEST&pagingType=PAGE&page=0&size=20&videoType=REPLAY", token: token);
            foreach (var item in J.Items(root, "content", "data"))
            {
                if (ParseVideo(item) is { } video) media.Add(video);
                if (ParseBroadcast(item) is { } broadcast) broadcasts.Add(broadcast);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) { errors.Add("다시보기: " + e.Message); }
        try
        {
            media.AddRange((await clipsTask).Take(1));
        }
        catch (Exception e) when (e is not OperationCanceledException) { errors.Add("클립: " + e.Message); }
        var youtube = await youtubeTask;
        media.AddRange(youtube.Videos); errors.AddRange(youtube.Errors);
        return (live, followers, youtube, media, broadcasts, errors);
        async Task<IReadOnlyList<Media>> ReadClips() => ParseClips(await Json($"https://api.chzzk.naver.com/service/v1/channels/{Channel.Id}/clips?clipUID=&filterType=ALL&orderType=RECENT&size=15&readCount=", token: token));
    }

    public async Task<YouTubePublicResult> YouTubePublic(CancellationToken token = default)
    {
        async Task<(string? Body, string? Error)> Read(string url)
        {
            try { return (await Text(url, token: token).ConfigureAwait(false), null); }
            catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested) { return (null, e.Message); }
        }
        var pageTask = Read(Channel.YouTubeUrl + "/videos?hl=ko");
        var feedTask = Read($"https://www.youtube.com/feeds/videos.xml?channel_id={Channel.YouTubeId}");
        await Task.WhenAll(pageTask, feedTask).ConfigureAwait(false);
        var pageResponse = await pageTask; var feedResponse = await feedTask;
        var errors = new List<string>(); YouTubePage? page = null; var feed = new List<Media>();
        try
        {
            if (pageResponse.Body is { } html) page = YouTubePublicParser.ParsePage(html, Channel.YouTubeId);
            else errors.Add("YouTube 채널: " + pageResponse.Error);
            if (page is { Subscribers: null }) errors.Add("YouTube 구독자 수를 확인하지 못했습니다.");
        }
        catch (Exception e) when (e is not OperationCanceledException) { errors.Add("YouTube 채널: " + e.Message); }
        try
        {
            if (feedResponse.Body is { } xml) feed = YouTubePublicParser.ParseFeed(xml, Channel.YouTubeId);
        }
        catch (Exception e) when (e is not OperationCanceledException) { /* The channel page can still supply the latest video. */ }
        var result = YouTubePublicParser.Combine(page, feed, errors);
        if (result.LatestVideo is null) errors.Add("YouTube 최신 영상을 확인하지 못했습니다.");
        return result;
    }

    public async Task<List<Cheese>> CheeseHistory(bool full, CancellationToken token = default)
    {
        var result = new List<Cheese>();
        for (var year = Channel.Today.Year; year >= (full ? 2023 : Channel.Today.Year); year--)
        {
            for (var page = 0; page < (full ? 50 : 1); page++)
            {
                var root = await Json($"https://api.chzzk.naver.com/commercial/v1/product/purchase/history?page={page}&size=100&searchYear={year}", true, token);
                var items = J.Items(root, "content", "data").ToArray();
                result.AddRange(items.Select(ParseCheese).OfType<Cheese>());
                if (items.Length < 100 || page + 1 >= (J.Number(J.At(root, "content", "totalPages")) ?? 1)) break;
                await Task.Delay(200, token);
            }
        }
        return result.DistinctBy(x => x.Id).OrderByDescending(x => x.At).ToList();
    }

    public async Task<List<CafePost>> Cafe(bool notices = true, int page = 1, CancellationToken token = default)
    {
        var articles = (await CafeHistoryPage(page, token)).Articles.ToList();
        var root = await Json($"https://apis.naver.com/cafe-web/cafe-boardlist-api/v1/cafes/{Channel.CafeId}/notices/menus/0", true, token);
        ValidateCafeList(root);
        articles.AddRange(ParseCafe(root).Select(x => x with { Notice = true }));
        return articles.Where(x => x.Notice).DistinctBy(x => x.Id).ToList();
    }

    private static void ValidateCafeList(JsonElement root)
    {
        if (J.At(root, "result", "articleList").ValueKind != JsonValueKind.Array && J.At(root, "message", "result", "articleList").ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("카페 공지 목록을 확인하지 못했습니다.");
    }

    public async Task<CafeArticleAvailability> CafeAvailabilityCheck(string id, CancellationToken token = default)
    {
        if (CafeAvailability.Url(id) is not { } url) return CafeArticleAvailability.Unknown;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri(Channel.CafeUrl);
        if (CookieHeader is not null && await CookieHeader(new Uri(url)) is { Length: > 0 } cookies) request.Headers.TryAddWithoutValidation("Cookie", cookies);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound) return CafeArticleAvailability.Unknown;
        await response.Content.LoadIntoBufferAsync(1024 * 1024, timeout.Token);
        using var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        return CafeAvailability.Parse(root.RootElement, (int)response.StatusCode, id);
    }

    public async Task<CafeHistoryPage> CafeHistoryPage(int page, CancellationToken token = default)
    {
        if (page is < 1 or > 100_050) throw new ArgumentOutOfRangeException(nameof(page));
        var root = await Json($"https://apis.naver.com/cafe-web/cafe-boardlist-api/v1/cafes/{Channel.CafeId}/menus/22/articles?page={page}&pageSize=20&viewType=L", true, token);
        var result = J.At(root, "result"); if (result.ValueKind != JsonValueKind.Object) result = J.At(root, "message", "result");
        if (J.At(result, "articleList").ValueKind != JsonValueKind.Array) throw new InvalidDataException("카페 글 목록을 읽지 못했습니다. 로그인과 카페 가입 여부를 확인해 주세요.");
        return new(ParseCafe(root).Select(x => x with { Notice = true }).ToArray(), J.Number(J.First(result, "totalArticleCount", "totalCount")));
    }

    public async Task<Broadcast?> BroadcastDetail(string videoId, CancellationToken token = default)
    {
        if (!Regex.IsMatch(videoId, "\\A[0-9]{1,100}\\z")) return null;
        var content = J.At(await Json($"https://api.chzzk.naver.com/service/v3/videos/{videoId}", token: token), "content");
        var record = ParseBroadcast(content);
        if (record?.VideoId != videoId) return null;
        return record with { DetailCheckedAt = DateTimeOffset.UtcNow };
    }

    public async Task<ReplayAvailability> BroadcastAvailabilityCheck(string videoId, CancellationToken token = default)
    {
        if (BroadcastAvailability.Url(videoId) is not { } url) return ReplayAvailability.Unknown;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        // Public visibility is authoritative even if the app has a privileged login.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri(Channel.Url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.NotFound)) return ReplayAvailability.Unknown;
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, timeout.Token);
        using var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        return BroadcastAvailability.Parse(root.RootElement, (int)response.StatusCode, videoId);
    }

    public async Task<List<Broadcast>> BroadcastHistory(CancellationToken token = default)
    {
        var result = new List<Broadcast>();
        for (var page = 0; page < 50; page++)
        {
            var root = await Json($"https://api.chzzk.naver.com/service/v1/channels/{Channel.Id}/videos?sortType=LATEST&pagingType=PAGE&page={page}&size=50&videoType=REPLAY", token: token);
            var items = J.Items(root, "content", "data").ToArray();
            result.AddRange(items.Select(ParseBroadcast).OfType<Broadcast>());
            if (items.Length < 50) break;
            await Task.Delay(250, token);
        }
        return result;
    }

    public static Media? ParseVideo(JsonElement item)
    {
        var id = J.String(item, "videoNo", "videoId");
        if (!Regex.IsMatch(id, @"^\d+$")) return null;
        return new Media(id, "치지직", "다시보기", J.String(item, "videoTitle", "title"), $"https://chzzk.naver.com/video/{id}",
            J.Image(item, "thumbnailImageUrl", "thumbnailUrl"), J.Date(J.First(item, "publishDate", "publishDateAt", "createdDate")));
    }

    public static IReadOnlyList<Media> ParseClips(JsonElement root)
    {
        var clips = new List<Media>();
        Visit(root, 0);
        return clips.DistinctBy(x => x.Id).OrderByDescending(x => x.PublishedAt).ToArray();
        void Visit(JsonElement item, int depth)
        {
            if (depth > 12 || clips.Count >= 100) return;
            if (item.ValueKind == JsonValueKind.Array) { foreach (var child in item.EnumerateArray()) Visit(child, depth + 1); }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var id = J.String(item, "clipUID", "clipUid"); var title = J.String(item, "clipTitle", "title");
                if (Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{1,100}\z") && !string.IsNullOrWhiteSpace(title))
                    clips.Add(new Media("clip:" + id, "치지직", "클립", title[..Math.Min(title.Length, 300)], $"https://chzzk.naver.com/clips/{id}",
                        J.Image(item, "thumbnailImageUrl", "thumbnailUrl", "clipThumbnailImageUrl"), J.Date(J.First(item, "createdDate", "createdAt", "publishDate", "publishDateAt"))));
                else foreach (var property in item.EnumerateObject()) Visit(property.Value, depth + 1);
            }
        }
    }

    public static Broadcast? ParseBroadcast(JsonElement item)
    {
        var type = J.String(item, "videoType").ToUpperInvariant(); if (type is not ("" or "REPLAY" or "VOD")) return null;
        var media = ParseVideo(item); if (media is null) return null;
        var start = J.Date(J.First(item, "liveOpenDate", "liveStartDate", "openDate", "startedAt"));
        var end = J.Date(J.First(item, "liveCloseDate", "liveEndDate", "closeDate", "endedAt"));
        var seconds = J.Number(J.First(item, "duration", "videoDuration", "durationSeconds")) ?? 0;
        var exact = start is not null && end > start && (end.Value - start.Value).TotalDays <= 31;
        if (exact) seconds = (long)(end!.Value - start!.Value).TotalSeconds;
        if (seconds is < 0 or > 2678400) return null;
        var liveId = J.String(item, "liveId", "liveNo");
        if (liveId.Length == 0 && J.String(item, "liveRewindPlaybackJson") is { Length: > 0 and <= 2_000_000 } playback)
        {
            try { using var data = JsonDocument.Parse(playback); liveId = J.String(J.At(data.RootElement, "meta"), "liveId"); } catch (JsonException) { }
        }
        return BroadcastLogic.Normalize(new Broadcast { Id = "vod:" + media.Id, VideoId = media.Id, Title = media.Title, StartedAt = start, EndedAt = exact ? end : null,
            PublishedAt = media.PublishedAt, LiveSessionId = Regex.IsMatch(liveId, "\\A[A-Za-z0-9_-]{1,100}\\z") ? "id-" + liveId : null,
            Seconds = seconds, VodSeconds = J.Number(J.First(item, "duration", "videoDuration", "durationSeconds")), Url = media.Url, ImageUrl = media.ImageUrl, TimingSource = exact ? "liveTiming" : "vodDuration" });
    }

    public static Cheese? ParseCheese(JsonElement item)
    {
        var amount = J.Number(J.First(item, "payAmount", "totalAmount"));
        var at = J.Date(J.First(item, "purchaseDate", "createdTime"));
        if (amount is null or <= 0 || at is null) return null;
        var message = J.String(item, "donationText", "donationMessage", "missionText");
        var id = J.String(item, "purchaseId", "purchaseNo", "donationId", "transactionId", "id");
        if (id.Length == 0) id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{at:O}|{amount}|{J.String(item, "channelId")}|{message}")));
        return new Cheese(id, J.String(item, "channelName", "hostChannelName"), amount.Value, message, J.String(item, "donationType"), at.Value);
    }

    public static IEnumerable<CafePost> ParseCafe(JsonElement root)
    {
        foreach (var outer in J.Items(root, "result", "articleList").Concat(J.Items(root, "message", "result", "articleList")))
        {
            var type = J.String(outer, "type").ToUpperInvariant();
            if (type != "" && type != "ARTICLE" && !type.Contains("NOTICE")) continue;
            var item = J.At(outer, "item"); if (item.ValueKind != JsonValueKind.Object) item = outer;
            var id = J.String(item, "articleId", "articleid"); var title = J.String(item, "subject");
            var at = J.Date(J.First(item, "writeDateTimestamp", "writeDate", "createdAt"));
            if (!Regex.IsMatch(id, @"^\d+$") || title.Length == 0 || at is null) continue;
            var author = J.String(J.At(item, "writerInfo"), "nickName", "nickname");
            if (author.Length == 0) author = J.String(item, "writerNickname", "writerName");
            var menu = J.String(item, "menuName");
            var notice = type.Contains("NOTICE") || J.Bool(J.At(item, "isNotice")) || J.Bool(J.At(item, "notice")) || J.String(item, "menuId", "menuid") == "22" || menu.Contains("공지");
            yield return new CafePost(id, title, author, menu, notice, at.Value, $"https://cafe.naver.com/f-e/cafes/{Channel.CafeId}/articles/{id}");
        }
    }

    public static bool SafeImage(string url) => url.Length <= 4096 && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        new[] { "pstatic.net", "naver.net", "naver.com", "ytimg.com" }.Any(host => uri.Host == host || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    public async Task<byte[]> Image(string url, CancellationToken token = default)
    {
        if (!SafeImage(url)) throw new InvalidOperationException("허용되지 않은 썸네일 주소입니다.");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(8 * 1024 * 1024, token);
        return await response.Content.ReadAsByteArrayAsync(token);
    }
    public void Dispose() => http.Dispose();
}

public static class J
{
    public static JsonElement At(JsonElement value, params string[] path)
    {
        foreach (var key in path) if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value)) return default;
        return value;
    }
    public static JsonElement First(JsonElement value, params string[] keys) => keys.Select(key => At(value, key)).FirstOrDefault(x => x.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined));
    public static string String(JsonElement value, params string[] keys)
    {
        if (keys.Length > 0) value = First(value, keys);
        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? "" : value.ToString();
    }
    public static long? Number(JsonElement value) => double.TryParse(String(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n >= long.MinValue && n < long.MaxValue ? (long)n : null;
    public static bool Bool(JsonElement value) => new[] { "true", "1", "y", "yes", "notice" }.Contains(String(value).ToLowerInvariant());
    public static IEnumerable<JsonElement> Items(JsonElement value, params string[] path)
    {
        value = At(value, path); return value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
    }
    public static DateTimeOffset? Date(JsonElement value)
    {
        var text = String(value).Trim(); if (text.Length == 0) return null;
        if (double.TryParse(text, CultureInfo.InvariantCulture, out var epoch) && epoch > 1_000_000_000)
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds((long)(epoch < 10_000_000_000 ? epoch * 1000 : epoch)); } catch (ArgumentOutOfRangeException) { return null; }
        }
        if (!Regex.IsMatch(text, @"(?:Z|[+-]\d{2}:?\d{2})$", RegexOptions.IgnoreCase)) text += "+09:00";
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }
    public static string Image(JsonElement value, params string[] keys)
    {
        var url = String(value, keys).Replace("{type}", "480"); return ApiClient.SafeImage(url) ? url : "";
    }
}
