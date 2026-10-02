using System.Text.Json;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private void VerifyStorageMaintenance(Action<bool, string> check)
    {
        var directory = Path.Combine(app.Store.DirectoryPath, "storage-fixture");
        var root = Path.Combine(directory, "updates");
        var target = Path.Combine(directory, "JjogaeStatus.exe");
        Directory.CreateDirectory(root); File.WriteAllText(target, "installed fixture");
        var now = DateTimeOffset.UtcNow;
        (string Stage, UpdatePlan Plan) Stage(string version = "0.4.13", bool completed = true, string? otherTarget = null)
        {
            var stage = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            var plan = new UpdatePlan(otherTarget ?? target, "0.4.12", version, new string('a', 64), 1, 0, Guid.NewGuid().ToString("N"));
            File.WriteAllText(Path.Combine(stage, "plan.json"), JsonSerializer.Serialize(plan, StateStore.Json));
            File.WriteAllText(Path.Combine(stage, "JjogaeStatus.exe"), "downloaded fixture");
            File.WriteAllText(Path.Combine(stage, "updater.exe"), "helper fixture");
            if (completed) File.WriteAllText(Path.Combine(stage, "healthy-" + plan.Token), plan.Version);
            return (stage, plan);
        }
        void Cleanup(params string[] running) => UpdateCleanup.Run(directory, target, new Version("0.4.14"), running, now);
        var success = Stage();
        var backup = Path.Combine(directory, ".JjogaeStatus.previous-" + success.Plan.Token + ".exe"); File.WriteAllText(backup, "previous fixture");
        var records = Path.Combine(directory, "state.json"); File.WriteAllText(records, "user records fixture");
        Cleanup();
        check(!Directory.Exists(success.Stage), "storage / completed update staging files are removed");
        check(!File.Exists(backup), "storage / confirmed update removes its paired rollback copy");
        check(File.ReadAllText(target) == "installed fixture" && File.ReadAllText(records) == "user records fixture", "storage / maintenance preserves installed app and user records");
        var active = Stage(); Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(Directory.Exists(active.Stage), "storage / running helper protects its staging directory");
        var unknown = Stage(); File.WriteAllText(Path.Combine(unknown.Stage, "personal.txt"), "preserve"); Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(File.Exists(Path.Combine(unknown.Stage, "personal.txt")), "storage / unexpected file prevents stage cleanup");
        var pending = Stage(completed: false); Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(Directory.Exists(pending.Stage), "storage / recent unconfirmed update is retained for recovery");
        var future = Stage("0.4.15"); Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(Directory.Exists(future.Stage), "storage / newer update is never removed by an older app");
        var other = Stage(otherTarget: Path.Combine(directory, "other", "JjogaeStatus.exe")); Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(Directory.Exists(other.Stage), "storage / another installation's stage is preserved");
        var corrupt = Stage(); File.WriteAllText(Path.Combine(corrupt.Stage, "plan.json"), "{bad"); Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(Directory.Exists(corrupt.Stage), "storage / malformed plan does not authorize cleanup");
        var wrongHealth = Stage(); File.WriteAllText(Path.Combine(wrongHealth.Stage, "healthy-" + wrongHealth.Plan.Token), "0.4.99"); Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(Directory.Exists(wrongHealth.Stage), "storage / mismatched health receipt does not authorize cleanup");
        var abandoned = Stage(completed: false); File.SetLastWriteTimeUtc(Path.Combine(abandoned.Stage, "plan.json"), now.AddDays(-8).UtcDateTime);
        var abandonedBackup = Path.Combine(directory, ".JjogaeStatus.previous-" + abandoned.Plan.Token + ".exe"); File.WriteAllText(abandonedBackup, "preserve rollback");
        Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(!Directory.Exists(abandoned.Stage) && File.Exists(abandonedBackup), "storage / week-old abandoned downloads are removed while unconfirmed rollback is preserved");
        var partial = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(partial); var partialFile = Path.Combine(partial, "JjogaeStatus.exe"); File.WriteAllText(partialFile, "partial");
        Cleanup(Path.Combine(active.Stage, "updater.exe")); check(Directory.Exists(partial), "storage / recent partial download is preserved");
        File.SetLastWriteTimeUtc(partialFile, now.AddDays(-2).UtcDateTime); Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(!Directory.Exists(partial), "storage / abandoned partial download is removed after a day");
        var nested = Stage(); Directory.CreateDirectory(Path.Combine(nested.Stage, "user-folder")); Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(Directory.Exists(nested.Stage), "storage / nested directories prevent cleanup");
        var link = Stage(); var outside = Path.Combine(directory, "outside.txt"); File.WriteAllText(outside, "outside fixture");
        Junction(Path.Combine(link.Stage, "linked"), directory);
        Cleanup(Path.Combine(active.Stage, "updater.exe"));
        check(Directory.Exists(link.Stage) && File.ReadAllText(outside) == "outside fixture", "storage / linked directory and its destination are preserved");
        var meter = Path.Combine(directory, "meter"); Directory.CreateDirectory(meter);
        File.WriteAllBytes(Path.Combine(meter, "state.json"), new byte[11]);
        foreach (var (folder, size) in new[] { ("thumbnails", 13), ("WebView2", 17), ("YouTubeWebView2", 19), ("updates", 23) })
        { Directory.CreateDirectory(Path.Combine(meter, folder)); File.WriteAllBytes(Path.Combine(meter, folder, "fixture"), new byte[size]); }
        var usage = StorageUsage.Read(meter);
        check(usage is { Records: 11, Images: 13, Browser: 36, Updates: 23, Total: 83, Incomplete: false }, "storage / usage separates records images browser data and updates");
        Junction(Path.Combine(meter, "linked"), root);
        check(StorageUsage.Read(meter).Total == 83, "storage / usage does not traverse linked directories");
        var store = new StateStore(Path.Combine(directory, "compact")); var state = new AppState();
        state.Settings.Theme = "dark"; store.Save(state);
        check(store.Load().Settings.Theme == "dark" && new FileInfo(store.StatePath).Length < System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(state, StateStore.Json)), "storage / compact storage preserves reloadable state with fewer bytes");
        var export = Path.Combine(directory, "export.json"); StateStore.Export(state, export);
        check(StateStore.Import(export).Settings.Theme == "dark" && File.ReadAllText(export).Contains('\n'), "storage / exported backup remains readable and importable");
    }
    private static void Junction(string link, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        { Arguments = $"/d /c mklink /J \"{link}\" \"{target}\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = System.Diagnostics.Process.Start(start)!;
        if (!process.WaitForExit(5000) || process.ExitCode != 0) throw new IOException("Could not create the isolated junction fixture.");
    }
}
