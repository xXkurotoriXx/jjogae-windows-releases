using System.Text.Json;
using Jjogae.Core;

var checks = 0;
void Check(bool condition, string name) { checks++; if (!condition) throw new Exception("FAIL: " + name); }
var fresh = new AppState();
AppUpdateTests.Run(Check);
using (var clipFixture = JsonDocument.Parse("""{"content":{"data":[{"clipUID":"abc_123","clipTitle":"최신 클립","createdDate":"2026-09-24T12:00:00+09:00"},{"clipUid":"old-1","title":"이전 클립","createdAt":"2026-09-23T12:00:00+09:00"},{"clipUID":"../unsafe","clipTitle":"거부"}]}}"""))
{
    var clips = ApiClient.ParseClips(clipFixture.RootElement);
    Check(clips.Count == 2 && clips[0].Url == "https://chzzk.naver.com/clips/abc_123", "latest clip parses and orders safely");
    Check(clips.All(clip => clip.Kind == "클립" && clip.Id.StartsWith("clip:")), "clips are distinct from replay IDs");
    var clipState = new AppState { MediaBaseline = true, Settings = new Preferences { Notifications = true } };
    Policies.ObserveMedia(clipState, clips, null, null, null, DateTimeOffset.UtcNow);
    Check(clipState.Media.Count == 2 && clipState.Pending.Count == 0, "clip display does not create unsolicited notifications");
}
Check(fresh.Account is null, "fresh account");
Check(fresh.Cheese.Count == 0 && fresh.Cafe.Count == 0 && fresh.Broadcasts.Count == 0 && fresh.Pending.Count == 0 && fresh.NotificationKeys.Count == 0, "empty histories");
Check(fresh.Settings.YouTubeSubscribedOn is null && fresh.Settings.YouTubeMemberSince is null && fresh.YouTubeSubscribers is null, "YouTube fields empty");
Check(!fresh.Settings.Notifications && fresh.Settings.PageSize == 10, "safe defaults");
foreach (var percent in new[] { 0, 1, 25, 50, 75, 99, 100 })
{
    var preferences = new Preferences { BackgroundOpacity = percent / 100d };
    Check(Math.Abs(preferences.BackgroundOpacity - percent / 100d) < .00001, "opacity percent maps directly to the image");
    var restored = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences, StateStore.Json), StateStore.Json)!;
    Check(Math.Abs(restored.BackgroundOpacity - percent / 100d) < .00001, "opacity persists in existing backups");
}
var oldPreferences = JsonSerializer.Deserialize<Preferences>("{\"transparency\":0.6}", StateStore.Json)!;
Check(Math.Abs(oldPreferences.BackgroundOpacity - .4) < .00001, "existing transparency is converted to opacity");
Check(new Preferences { BackgroundOpacity = double.NaN }.BackgroundOpacity == .4 && new Preferences { BackgroundOpacity = 2 }.BackgroundOpacity == 1 && new Preferences { BackgroundOpacity = -1 }.BackgroundOpacity == 0, "invalid opacity is normalized");
Check(!fresh.Settings.YouTubeMemberActive, "membership hidden by default");
foreach (var count in new[] { 0, 1, 10, 11, 20, 99, 100, 101, 900 })
foreach (var size in new[] { 10, 20, 50, 100 })
{
    var items = Enumerable.Range(0, count).ToArray();
    Check(Policies.Page(items, 0, size).Count == Math.Min(count, size), "first page");
    Check(Policies.Page(items, -1, size).SequenceEqual(Policies.Page(items, 0, size)), "negative page clamp");
    var final = Policies.Page(items, int.MaxValue, size);
    Check(count == 0 ? final.Count == 0 : final.Count > 0 && final.Last() == count - 1, "last page clamp");
}
Check(Policies.Page(Enumerable.Range(0, 35).ToArray(), 0, 5).Count == 10, "invalid page size");
foreach (var (count, milestone) in new[] { (999L, 0L), (1000L, 1000L), (9999L, 9000L), (10000L, 10000L), (11000L, 10000L), (14999L, 10000L), (15000L, 15000L), (100000L, 100000L) })
    Check(Policies.Milestone(count) == milestone, "milestone scale");

var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(9));
CafePost Post(string id, string author = "테스트 작성자", bool notice = false, DateTimeOffset? at = null) => new(id, "테스트 글 " + id, author, "테스트 게시판", notice, at ?? now, "https://cafe.naver.com/f-e/cafes/31522940/articles/" + id);
var state = new AppState { Settings = new Preferences { Notifications = true } };
var historic = Enumerable.Range(1, 1210).Select(i => Post(i.ToString(), Channel.Name, true, now.AddDays(-20))).ToArray();
Policies.MergeCafe(state, historic, now);
Check(state.SeenCafe.Count == 1210 && state.Pending.Count == 0, "silent historical baseline");
var newPosts = new[] { Post("2001"), Post("2002", notice: true), Post("2003", Channel.Name), Post("2004", Channel.Name, true) };
Policies.MergeCafe(state, newPosts, now.AddSeconds(60));
Check(state.Pending.Count == 3, "protected posts only, combined condition once");
Policies.MergeCafe(state, newPosts, now.AddSeconds(120));
Check(state.Pending.Count == 3, "repeat fetch dedupe");
Policies.OpenCafe(state, newPosts[0]); Check(state.ReadCafe.Contains("2001"), "ordinary post opened read");
Policies.OpenCafe(state, newPosts[1]); Check(!state.ReadCafe.Contains("2002"), "notice preserved");
Policies.OpenCafe(state, newPosts[2]); Check(!state.ReadCafe.Contains("2003"), "Rupa preserved");
Policies.MarkCafeRead(state, ["2002"]); Check(state.ReadCafe.Contains("2002") && state.Pending.All(x => x.Id != "cafe:2002"), "explicit protected read cancels pending");
var beforeRestore = state.Pending.Count;
Policies.MergeCafe(state, [Post("2002", Channel.Name, true)], now.AddSeconds(150), true);
Check(!state.ReadCafe.Contains("2002") && state.Pending.Count == beforeRestore, "restore no notification");
Policies.MergeCafe(state, [Post("3000", Channel.Name, true, now.AddDays(-5))], now.AddSeconds(180));
Check(state.Pending.Count == beforeRestore, "old post excluded");
state.Settings.Notifications = false;
Policies.MergeCafe(state, [Post("4000", Channel.Name, true)], now.AddSeconds(200));
Check(state.Pending.Count == beforeRestore, "disabled no new delivery");
state.Settings.Notifications = true;
Policies.MergeCafe(state, [Post("4000", Channel.Name, true)], now.AddSeconds(220));
Check(state.Pending.Count == beforeRestore, "reenable no replay");

using var parsed = JsonDocument.Parse("""{"result":{"articleList":[{"type":"NOTICE","item":{"articleId":123,"subject":"테스트 공지","writerInfo":{"nickName":"아홀로 루파"},"writeDate":"2026-09-01 12:00:00"}}]}}""");
var article = ApiClient.ParseCafe(parsed.RootElement).Single();
Check(article.Notice && article.Rupa && article.At == now, "cafe parse KST and protected flags");
using var cheeseJson = JsonDocument.Parse("""{"purchaseId":"example-1","payAmount":1000,"purchaseDate":"2026-09-01 12:00:00","channelName":"테스트","donationText":"테스트 메모"}""");
Check(ApiClient.ParseCheese(cheeseJson.RootElement) is { Amount: 1000, At: var at } && at == now, "cheese parse");
using var noDate = JsonDocument.Parse("""{"payAmount":1000}""");
Check(ApiClient.ParseCheese(noDate.RootElement) is null, "do not invent date");
foreach (var url in new[] { "http://i.ytimg.com/a", "https://i.ytimg.com.attacker.test/a", "file:///secret", "https://127.0.0.1/a", "https://ytimg.com:44/a" })
    Check(!ApiClient.SafeImage(url), "unsafe thumbnail rejected");
Check(ApiClient.SafeImage("https://i.ytimg.com/vi/abcdefghijk/mqdefault.jpg"), "official thumbnail allowed");

var record = new Broadcast { Id = "vod:1", Title = "테스트 방송", StartedAt = new DateTimeOffset(2026, 8, 31, 23, 0, 0, TimeSpan.FromHours(9)), Seconds = 7200 };
var days = Policies.DailySeconds([record]);
Check(days.Count == 1 && days[new DateOnly(2026, 8, 31)] == 7200, "Mac parity: whole broadcast belongs to its Korean start day");
Policies.MergeBroadcast(state, record);
Policies.MergeBroadcast(state, record with { Seconds = 5400 });
Check(state.Broadcasts.Count == 1 && state.Broadcasts[0].Seconds == 5400, "vod update dedupe");
Policies.MergeBroadcast(state, record with { Seconds = 6000, TimingSource = "liveTiming" });
Policies.MergeBroadcast(state, record with { Seconds = 7200, TimingSource = "vod" });
Check(state.Broadcasts[0].Seconds == 6000, "service timing wins");
Policies.ObserveMedia(state, [], null, null, null, now);
Check(state.Broadcasts.Count == 1, "deleted remote video retained");
var observed = new AppState { Settings = new Preferences { Notifications = true } };
Policies.ObserveMedia(observed, [], new LiveObservation(true, "test-live", "테스트 방송", now, null, ""), 9000, null, now);
Policies.ObserveMedia(observed, [], new LiveObservation(false, "test-live", "테스트 방송", null, null, ""), 11000, null, now.AddHours(12));
Check(observed.Broadcasts.Count == 1 && Policies.DailySeconds(observed.Broadcasts).Count == 0, "offline gap retained pending without counting its duration");
Check(observed.Pending.Count(x => x.Kind == "달성") == 1, "10k milestone");
Policies.ObserveMedia(observed, [], null, 12000, null, now.AddHours(13));
Check(observed.Pending.Count(x => x.Kind == "달성") == 1, "no 11k or12k alert");
Check(Channel.BirthdayRemaining(new DateOnly(2026, 7, 23)) == 0, "birthday same day");
Check(Channel.BirthdayRemaining(new DateOnly(2026, 7, 24)) == 364, "next birthday");

var directory = Path.Combine(Path.GetTempPath(), "jjogae-core-test-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new StateStore(directory);
    Check(store.Load().Cheese.Count == 0, "fresh store");
    state.Account = new Account("synthetic-runtime-only", 1, "test", null, "");
    store.Save(state);
    var json = File.ReadAllText(store.StatePath);
    Check(!json.Contains("synthetic-runtime-only"), "account not serialized");
    var loaded = store.Load(); Check(loaded.Cafe.Count == state.Cafe.Count && loaded.SeenCafe.SetEquals(state.SeenCafe), "persistence dedupe");
    var backup = Path.Combine(directory, "export.json"); StateStore.Export(state, backup);
    Check(!File.ReadAllText(backup).Contains("synthetic-runtime-only"), "export no account");
    var imported = StateStore.Import(backup); Check(imported.Pending.Count == 0 && imported.Account is null && !imported.CafeBaseline, "safe import");
    File.WriteAllText(backup, "{\"readCafeArticleIDs\":[]}");
    var rejected = false; try { StateStore.Import(backup); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, "mac snapshot rejected");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
NotificationTests.Run(Check);
await BroadcastAvailabilityTests.Run(Check);
YouTubeTests.Run(Check);
await YouTubeTests.RunNetwork(Check);
await ParityTests.Run(Check);
MacParityTests.Run(Check);
await WebConnectionTests.Run(Check);
if (args.Contains("--youtube-live"))
{
    using var api = new ApiClient();
    var cookiesRequested = false;
    api.CookieHeader = _ => { cookiesRequested = true; throw new Exception("Public YouTube must not request login cookies."); };
    var youtube = await api.YouTubePublic();
    Check(!cookiesRequested, "live public YouTube does not use login");
    Check(youtube.Subscribers > 0, "live public subscriber count");
    Check(youtube.LatestVideo is not null, "live public latest video");
    Console.WriteLine($"Public YouTube: subscribers={youtube.Subscribers}, video={youtube.LatestVideo?.Id}, dated videos={youtube.Videos.Count(v => v.PublishedAt is not null)}, warnings={youtube.Errors.Count}");
}
Console.WriteLine($"PASS: {checks} checks");
