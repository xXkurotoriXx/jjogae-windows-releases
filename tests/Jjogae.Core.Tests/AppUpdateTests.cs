using System.Text.Json;
using Jjogae.Core;

internal static class AppUpdateTests
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        check(AppUpdates.Due(null, now) && AppUpdates.Due(now.AddDays(-7), now), "updates check on first launch and after one week");
        check(!AppUpdates.Due(now.AddDays(-6), now) && AppUpdates.Due(now.AddDays(1), now), "weekly schedule handles recent check and clock rollback");
        check(AppUpdates.VersionTag("v0.4.6") == new Version(0, 4, 6) && AppUpdates.VersionTag("v0.4.6-beta") is null, "only supported version tags install");
        var hash = new string('a', 64);
        var publicRelease = new AppRelease(new Version(0, 4, 15), 1, 2, 100, hash);
        check(publicRelease.DownloadUrl == "https://github.com/xXkurotoriXx/jjogae-windows-releases/releases/download/v0.4.15/JjogaeStatus.exe"
            && publicRelease.ChecksumsUrl.EndsWith("/v0.4.15/SHA256SUMS.txt"), "public update downloads use anonymous GitHub browser asset URLs");
        object Release(string version, bool draft = false, string digest = "", string name = "JjogaeStatus.exe", bool prerelease = false) => new
        {
            tag_name = version, draft, prerelease, published_at = "2026-09-28T00:00:00Z",
            assets = new[] {
                new {name, id=1, state="uploaded", digest="sha256:" + (digest == "" ? hash : digest), size=100},
                new {name="SHA256SUMS.txt", id=2, state="uploaded", digest="sha256:"+hash, size=80}
            }
        };
        using var releases = JsonDocument.Parse(JsonSerializer.Serialize(new[] { Release("v0.4.4"), Release("v0.4.6"), Release("v0.4.7", true), Release("v0.4.8", digest:"bad"), Release("v0.4.9", name:"other.exe"), Release("v0.4.10", prerelease:true) }));
        check(AppUpdates.Select(releases.RootElement, new Version(0, 4, 5))?.Version == new Version(0, 4, 6), "public updates select newest verified stable release and exclude drafts and prereleases");
        check(AppUpdates.Select(releases.RootElement, new Version(0, 4, 6)) is null, "updater never downgrades or reinstalls current version");
        check(AppUpdates.MatchesChecksum("\uFEFF" + hash + "  JjogaeStatus.exe\r\n", hash), "release checksums accept UTF8 BOM and Windows line endings");
        check(!AppUpdates.MatchesChecksum(hash + "  other.exe", hash) && !AppUpdates.MatchesChecksum(new string('b', 64) + "  JjogaeStatus.exe", hash), "wrong asset or checksum blocks install");
        var directory = Path.Combine(Path.GetTempPath(), "Jjogae-update-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "JjogaeStatus.exe"); var stage = Path.Combine(directory, "new.exe");
            var backup = Path.Combine(directory, "previous.exe"); var failed = Path.Combine(directory, "failed.exe");
            File.WriteAllText(target, "original executable"); File.WriteAllText(stage, "new executable");
            var settings = Path.Combine(directory, "settings.json"); File.WriteAllText(settings, "preserved user settings");
            try { AppUpdates.Replace(stage, target, backup, hash); check(false, "corrupt download rejected"); }
            catch (InvalidDataException) { check(File.ReadAllText(target) == "original executable" && !File.Exists(backup), "checksum failure leaves original executable untouched"); }
            AppUpdates.Replace(stage, target, backup, AppUpdates.Hash(stage));
            check(File.ReadAllText(target) == "new executable" && File.ReadAllText(backup) == "original executable", "atomic update retains recoverable executable");
            AppUpdates.Rollback(backup, target, failed);
            check(File.ReadAllText(target) == "original executable" && File.ReadAllText(failed) == "new executable", "failed startup can roll back to original executable");
            check(File.ReadAllText(settings) == "preserved user settings", "update replacement and rollback preserve user settings");
        }
        finally { Directory.Delete(directory, true); }
    }
}
