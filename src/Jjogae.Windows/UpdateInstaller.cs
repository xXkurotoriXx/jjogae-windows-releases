using System.Diagnostics;
using System.Text.Json;

namespace Jjogae.Windows;

internal sealed record UpdatePlan(string Target, string PreviousVersion, string Version, string Sha256, int ParentId, long ParentStarted, string Token);

internal static class UpdateInstaller
{
    internal static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JjogaeWindows", "updates");
    private static (UpdatePlan Plan, string Stage) Read(string path)
    {
        var full = Path.GetFullPath(path); var stage = Path.GetDirectoryName(full)!;
        if (Path.GetFileName(full) != "plan.json" || !Path.GetDirectoryName(stage)!.Equals(Root, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(stage), "N", out _) || (File.GetAttributes(stage) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("업데이트 경로가 올바르지 않습니다.");
        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(full), StateStore.Json) ?? throw new InvalidDataException("업데이트 정보를 읽지 못했습니다.");
        if (!Path.IsPathFullyQualified(plan.Target) || Path.GetFileName(plan.Target) != "JjogaeStatus.exe"
            || !Guid.TryParseExact(plan.Token, "N", out _) || !AppUpdates.ValidHash(plan.Sha256)
            || !Version.TryParse(plan.PreviousVersion, out var previous) || !Version.TryParse(plan.Version, out var version) || version <= previous)
            throw new InvalidDataException("업데이트 정보가 올바르지 않습니다.");
        return (plan, stage);
    }
    public static async Task<int> Run(string path)
    {
        UpdatePlan? plan = null; string? stage = null; string? backup = null; Process? launched = null;
        var replaced = false;
        try
        {
            (plan, stage) = Read(path);
            var stagedExe = Path.Combine(stage, "JjogaeStatus.exe");
            if ((File.GetAttributes(stagedExe) & FileAttributes.ReparsePoint) != 0 || AppUpdates.Hash(stagedExe) != plan.Sha256)
                throw new InvalidDataException("업데이트 파일 검증에 실패했습니다.");
            if (FileVersionInfo.GetVersionInfo(stagedExe).FileVersion is not { } text || !Version.TryParse(text, out var fileVersion)
                || fileVersion.ToString(3) != plan.Version) throw new InvalidDataException("업데이트 버전이 일치하지 않습니다.");
            Process? parent = null;
            try { parent = Process.GetProcessById(plan.ParentId); } catch (ArgumentException) { }
            if (parent is not null)
            {
                using (parent)
                {
                    if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStarted)
                    {
                        if (!string.Equals(parent.MainModule?.FileName, plan.Target, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("실행 중인 앱 경로가 일치하지 않습니다.");
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                        await parent.WaitForExitAsync(timeout.Token);
                    }
                }
            }
            var targetDirectory = Path.GetDirectoryName(plan.Target)!;
            var onVolume = Path.Combine(targetDirectory, ".JjogaeStatus.update-" + plan.Token + ".exe");
            backup = Path.Combine(targetDirectory, ".JjogaeStatus.previous-" + plan.Token + ".exe");
            File.Copy(stagedExe, onVolume, false);
            AppUpdates.Replace(onVolume, plan.Target, backup, plan.Sha256); replaced = true;
            var start = new ProcessStartInfo(plan.Target) { UseShellExecute = false, WorkingDirectory = targetDirectory };
            start.ArgumentList.Add("--update-plan"); start.ArgumentList.Add(path);
            launched = Process.Start(start) ?? throw new InvalidOperationException("새 버전을 실행하지 못했습니다.");
            var health = Path.Combine(stage, "healthy-" + plan.Token);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
            while (!File.Exists(health) && !launched.HasExited && DateTimeOffset.UtcNow < deadline) await Task.Delay(200);
            if (!File.Exists(health)) throw new InvalidOperationException("새 버전이 실행을 확인하지 못했습니다.");
            Receipt(plan, true, "업데이트 완료");
            return 0;
        }
        catch (Exception)
        {
            if (replaced && plan is not null && backup is not null)
            {
                if (launched is not null && !launched.HasExited)
                {
                    launched.CloseMainWindow();
                    if (!launched.WaitForExit(5000)) { launched.Kill(); launched.WaitForExit(5000); }
                }
                AppUpdates.Rollback(backup, plan.Target, Path.Combine(Path.GetDirectoryName(plan.Target)!, ".JjogaeStatus.failed-" + plan.Token + ".exe"));
                _ = Process.Start(new ProcessStartInfo(plan.Target) { UseShellExecute = false });
            }
            else if (plan is not null && File.Exists(plan.Target))
            {
                var running = Process.GetProcessesByName("JjogaeStatus");
                try
                {
                    if (!running.Any(process => string.Equals(process.MainModule?.FileName, plan.Target, StringComparison.OrdinalIgnoreCase)))
                        _ = Process.Start(new ProcessStartInfo(plan.Target) { UseShellExecute = false });
                }
                finally { foreach (var process in running) process.Dispose(); }
            }
            if (plan is not null) Receipt(plan, false, "업데이트 실패 · 기존 버전을 유지하거나 복원했습니다.");
            return 1;
        }
        finally { launched?.Dispose(); }
    }
    private static void Receipt(UpdatePlan plan, bool success, string status)
    {
        var destination = Path.Combine(Path.GetDirectoryName(Root)!, "update-result.json");
        File.WriteAllText(destination + ".tmp", JsonSerializer.Serialize(new { plan.PreviousVersion, plan.Version, success, status, at = DateTimeOffset.UtcNow }, StateStore.Json));
        File.Move(destination + ".tmp", destination, true);
    }
    public static bool ConfirmHealth(string[] args)
    {
        var index = Array.IndexOf(args, "--update-plan"); if (index < 0 || index + 1 >= args.Length) return false;
        var (plan, stage) = Read(args[index + 1]);
        if (string.Equals(Environment.ProcessPath, plan.Target, StringComparison.OrdinalIgnoreCase) && WindowsUpdater.CurrentVersion.ToString(3) == plan.Version
            && AppUpdates.Hash(Environment.ProcessPath!) == plan.Sha256)
        {
            File.WriteAllText(Path.Combine(stage, "healthy-" + plan.Token), plan.Version);
            return true;
        }
        return false;
    }
}
