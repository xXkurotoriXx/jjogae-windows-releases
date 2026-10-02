using System.Diagnostics;
using System.Text.Json;

namespace Jjogae.Windows;

internal static class UpdateCleanup
{
    // Only our flat, GUID-named staging folders are eligible. Never follow links
    // or remove a folder containing an unexpected file or a running executable.
    internal static void Run(string directory, string target, Version current, IReadOnlyCollection<string>? running = null, DateTimeOffset? now = null)
    {
        var root = Path.GetFullPath(Path.Combine(directory, "updates"));
        try
        {
            if (!Directory.Exists(root) || Linked(root) || Linked(directory)) return;
            running ??= RunningExecutables();
            if (running is null) return;
            foreach (var stage in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (!Guid.TryParseExact(Path.GetFileName(stage), "N", out _) || Linked(stage)) continue;
                    var files = Directory.GetFileSystemEntries(stage);
                    if (files.Any(path => Directory.Exists(path) || Linked(path) || !Allowed(Path.GetFileName(path)))) continue;
                    if (running.Any(path => Path.GetDirectoryName(path)?.Equals(stage, StringComparison.OrdinalIgnoreCase) == true)) continue;
                    var planPath = Path.Combine(stage, "plan.json");
                    if (!File.Exists(planPath))
                    {
                        if (files.Any(path => (now ?? DateTimeOffset.UtcNow) - File.GetLastWriteTimeUtc(path) < TimeSpan.FromDays(1))) continue;
                    }
                    else
                    {
                        if (new FileInfo(planPath).Length > 65536) continue;
                        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath), StateStore.Json);
                        if (plan is null || !Path.IsPathFullyQualified(plan.Target) || !Path.GetFullPath(plan.Target).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)
                            || !Guid.TryParseExact(plan.Token, "N", out _) || !AppUpdates.ValidHash(plan.Sha256)
                            || !Version.TryParse(plan.PreviousVersion, out var previous) || !Version.TryParse(plan.Version, out var version) || version <= previous || version > current) continue;
                        var health = Path.Combine(stage, "healthy-" + plan.Token);
                        var completed = File.Exists(health) && new FileInfo(health).Length < 64 && File.ReadAllText(health) == plan.Version;
                        if (!completed && (now ?? DateTimeOffset.UtcNow) - File.GetLastWriteTimeUtc(planPath) < TimeSpan.FromDays(7)) continue;
                        if (completed)
                        {
                            var backup = Path.Combine(Path.GetDirectoryName(target)!, ".JjogaeStatus.previous-" + plan.Token + ".exe");
                            if (File.Exists(backup) && !Linked(backup) && !running.Contains(backup, StringComparer.OrdinalIgnoreCase)) File.Delete(backup);
                        }
                    }
                    foreach (var file in files) File.Delete(file);
                    Directory.Delete(stage, false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    internal static bool Allowed(string name) => name is "JjogaeStatus.exe" or "updater.exe" or "plan.json"
        || name.StartsWith("healthy-", StringComparison.Ordinal) && Guid.TryParseExact(name[8..], "N", out _);
    private static bool Linked(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static IReadOnlyCollection<string>? RunningExecutables()
    {
        var result = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.ProcessName is not ("JjogaeStatus" or "updater")) continue;
                    if (process.MainModule?.FileName is not { } path) return null;
                    result.Add(path);
                }
                catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { return null; }
            }
        }
        return result;
    }
}
