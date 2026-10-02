using System.Text.Json;
using System.Text.Json.Nodes;
using Jjogae.Core;

internal static class NotificationTests
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var (info, active) in new[] {
            ("null", false), ("{}", false), ("{\"status\":\"NONE\",\"tierName\":\"이전 티어\"}", false),
            ("{\"status\":\"EXPIRED\",\"tierNo\":1}", false), ("{\"status\":\"expired\",\"nextPublishYmdt\":\"2026-09-01\"}", false),
            ("{\"tierName\":\"아끼는 쪼개 티어\",\"nextPublishYmdt\":\"2026-10-01T12:00:00\"}", true),
            ("{\"tierNo\":1}", true), ("{\"nextPublishYmdt\":\"2026-10-01 12:00:00\"}", true) })
        {
            using var json = JsonDocument.Parse("{\"content\":{\"info\":" + info + "}}");
            var result = ChzzkSubscription.Parse(json.RootElement);
            check(result.Active == active, "subscription active state / " + info);
            check(active || result.Renewal.Length == 0, "expired subscription has no renewal");
        }
        using (var json = JsonDocument.Parse("""{"content":{"info":{"channelId":"different","tierName":"test"}}}"""))
        {
            var rejected = false; try { ChzzkSubscription.Parse(json.RootElement); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "other channel subscription rejected");
        }
        check(!new Account("test", null, "확인 중", null, "").IsSubscribed, "unconfirmed login does not show subscription card");
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.FromHours(9));
        var state = new AppState { Settings = new Preferences { Notifications = true } };
        Policies.ObserveMedia(state, [], new(false, "", "", null, null, ""), 9999, 9999, now);
        check(state.Pending.Count == 0, "initial notification baseline quiet");
        Policies.ObserveMedia(state, [new("test-video", "YouTube", "새 영상", "공개 테스트 영상", Channel.YouTubeUrl, "", now)],
            new(true, "test-live", "공개 테스트 방송", now, null, ""), 10000, 10000, now);
        check(state.Pending.Count == 4 && state.Pending.Count(x => x.Kind == "달성") == 2, "Windows alerts retain upload, live start and both milestones");
        Policies.ObserveMedia(state, [], new(false, "test-live", "", now, now.AddMinutes(125), ""), 11000, 11000, now.AddMinutes(125));
        check(state.Pending.Single(x => x.Kind == "방송 종료").Title.Contains("2시간 5분"), "live end alert retains duration");
        var birthday = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.FromHours(9));
        Policies.CalendarMilestones(state, birthday); Policies.CalendarMilestones(state, birthday.AddMinutes(1));
        check(state.Pending.Count(x => x.Kind == "생일") == 1, "birthday alert deduplicated without a log");
        var debut = new DateTimeOffset(Channel.Debut.AddDays(500).ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(9));
        Policies.CalendarMilestones(state, debut); Policies.CalendarMilestones(state, debut.AddMinutes(1));
        check(state.Pending.Count(x => x.Kind == "데뷔") == 1, "debut alert deduplicated without a log");
        var unique = new Notification("transient:one", "test", "test", "TRANSIENT_TITLE_ONLY", now, now);
        var delayedCafe = new AppState { Settings = new Preferences { Notifications = true }, CafeBaseline = true, CafeCheckedAt = now.AddMinutes(-1) };
        var delayed = new CafePost("late", "늦게 반영된 공지 🫧", Channel.Name, "공지", true, now.AddMinutes(-12), Channel.CafeUrl);
        Policies.MergeCafe(delayedCafe, [delayed, delayed with { Id = "old", At = now.AddDays(-2) }], now);
        check(delayedCafe.Pending.Count == 1 && delayedCafe.Pending[0].Id == "cafe:late", "delayed cafe indexing not lost after a newer successful poll; old history quiet");
        Policies.MergeCafe(delayedCafe, [delayed], now.AddMinutes(1));
        check(delayedCafe.Pending.Count == 1, "delayed cafe post deduplicated");
        var deliveryState = new AppState { Settings = new Preferences { Notifications = true }, Pending = [unique, unique with { Id = "second" }] };
        var delivery = new NotificationDelivery(); var attempts = 0;
        bool Reject(Notification _) { attempts++; return false; }
        bool Accept(Notification _) { attempts++; return true; }
        delivery.Drain(deliveryState, now, Reject);
        check(attempts == 1 && deliveryState.Pending.Count == 2, "rejected Windows notification retained for retry");
        delivery.Drain(deliveryState, now.AddSeconds(15), Accept);
        check(attempts == 1, "blocked notifications retry with backoff");
        delivery.Drain(deliveryState, now.AddMinutes(1), Accept);
        delivery.Drain(deliveryState, now.AddMinutes(1), Accept);
        check(attempts == 2 && deliveryState.Pending.Single().Id == "second", "concurrent refreshes cannot replace consecutive banners");
        delivery.Drain(deliveryState, now.AddSeconds(75), Accept);
        check(attempts == 3 && deliveryState.Pending.Count == 0, "next queued notification delivered after spacing");
        deliveryState.Pending.Add(unique with { DetectedAt = now.AddDays(-2) });
        delivery.Drain(deliveryState, now.AddMinutes(2), Accept);
        check(attempts == 3 && deliveryState.Pending.Count == 0, "expired retry discarded without stale banner");
        deliveryState.Pending.Add(unique); deliveryState.Settings.Notifications = false;
        delivery.Drain(deliveryState, now.AddMinutes(3), Accept);
        check(attempts == 3, "disabled preference prevents delivery");
        var sessions = new AppState { Settings = new Preferences { Notifications = true }, MediaBaseline = true, Live = new(true, "first", "first", null, null, "") };
        Policies.ObserveMedia(sessions, [], new(true, "second", "second", null, null, ""), null, null, now);
        check(sessions.Pending.Single().Kind == "방송 시작", "new live session detected even when service omits start time");
        Policies.ObserveMedia(sessions, [], null, null, null, now.AddMinutes(1));
        check(sessions.Live?.IsLive == true && sessions.Pending.Count == 1, "failed status read does not invent a broadcast end");
        Policies.QueueNotification(state, unique, true); Policies.QueueNotification(state, unique, true);
        check(state.Pending.Count(x => x.Id == unique.Id) == 1, "queued notification delivered once");
        var directory = Path.Combine(Path.GetTempPath(), "jjogae-notification-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new StateStore(directory); store.Save(state);
            CheckNoLog(File.ReadAllText(store.StatePath), "saved state");
            var reloaded = store.Load();
            check(reloaded.Pending.Count == 0 && reloaded.NotificationKeys.SetEquals(state.NotificationKeys), "only opaque dedupe keys survive restart");
            Policies.QueueNotification(reloaded, unique, true); Policies.CalendarMilestones(reloaded, birthday);
            check(reloaded.Pending.Count == 0, "restart does not replay earlier alerts");
            var export = Path.Combine(directory, "export.json"); StateStore.Export(state, export); CheckNoLog(File.ReadAllText(export), "export");
            var legacy = JsonSerializer.SerializeToNode(state, StateStore.Json)!;
            legacy["activities"] = JsonSerializer.SerializeToNode(new[] { unique }, StateStore.Json);
            legacy["pending"] = JsonSerializer.SerializeToNode(new[] { unique with { Id = "legacy:pending" } }, StateStore.Json);
            legacy["delivered"] = new JsonArray("legacy:delivered"); legacy["notificationKeys"] = new JsonArray();
            File.WriteAllText(store.StatePath, legacy.ToJsonString());
            var migrated = store.Load(); CheckNoLog(File.ReadAllText(store.StatePath), "legacy migration");
            check(migrated.Broadcasts.Count == state.Broadcasts.Count && migrated.Settings.Notifications, "migration preserves broadcast records and notification preference");
            foreach (var id in new[] { unique.Id, "legacy:pending", "legacy:delivered" }) Policies.QueueNotification(migrated, unique with { Id = id }, true);
            check(migrated.Pending.Count == 0 && migrated.NotificationKeys.Count == 3, "legacy IDs migrate without replaying or retaining titles");
            File.WriteAllText(export, legacy.ToJsonString()); var imported = StateStore.Import(export); store.Save(imported);
            CheckNoLog(File.ReadAllText(store.StatePath), "legacy import");
            check(imported.Pending.Count == 0 && imported.NotificationKeys.Count == 3, "legacy import remains silent");
            imported.Settings.Notifications = false; Policies.QueueNotification(imported, unique with { Id = "disabled" }, true);
            imported.Settings.Notifications = true; Policies.QueueNotification(imported, unique with { Id = "disabled" }, true);
            check(imported.Pending.Count == 0, "disabled alerts are not replayed when reenabled");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        void CheckNoLog(string json, string label)
        {
            using var parsed = JsonDocument.Parse(json);
            check(new[] { "activities", "pending", "delivered" }.All(field => !parsed.RootElement.TryGetProperty(field, out _)), label + " omits notification log fields");
            check(!json.Contains(unique.Title) && !json.Contains(unique.Id), label + " omits notification title and raw ID");
        }
    }
}
