using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Threading;

namespace Jjogae.Windows;

internal sealed class WindowsUpdater : IDisposable
{
    internal static Version CurrentVersion => new(Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));
    private readonly AppInstallation installation;
    private readonly string directory;
    private readonly Action shutdown;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMinutes(15) };
    private readonly CancellationTokenSource lifetime = new();
    public bool Busy { get; private set; }
    public string Status { get; private set; } = "새 버전은 1주일마다 자동으로 확인합니다.";
    public event Action? Changed;
    public WindowsUpdater(AppInstallation installation, string directory, Action shutdown)
    {
        this.installation = installation; this.directory = directory; this.shutdown = shutdown;
        timer.Tick += async (_, _) => { await Cleanup(); await Check(false); };
    }
    public void Start() { timer.Start(); _ = Maintain(); _ = Check(false); }
    private async Task Maintain()
    {
        try { await Task.Delay(TimeSpan.FromSeconds(10), lifetime.Token); await Cleanup(); }
        catch (OperationCanceledException) { }
    }
    private async Task Cleanup()
    {
        try { await Task.Run(() => UpdateCleanup.Run(directory, Environment.ProcessPath!, CurrentVersion), lifetime.Token); }
        catch (OperationCanceledException) { }
    }
    public void MarkUpdated()
    {
        installation.LastUpdateCheck = DateTimeOffset.UtcNow; installation.Save();
        SetStatus($"업데이트 완료 · 현재 버전 {CurrentVersion}");
    }
    public async Task Check(bool manual)
    {
        if (Busy || (!manual && (!installation.AutomaticUpdates || !AppUpdates.Due(installation.LastUpdateCheck, DateTimeOffset.UtcNow)
            || installation.LastAttempt is { } attempt && DateTimeOffset.UtcNow - attempt < TimeSpan.FromHours(6)))) return;
        Busy = true; installation.LastAttempt = DateTimeOffset.UtcNow; installation.Save(); SetStatus("업데이트 확인 중…");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("JjogaeStatus/" + CurrentVersion);
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{AppUpdates.Repository}/releases?per_page=100");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await client.SendAsync(request, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                throw new InvalidOperationException(PublicUpdateConnection.AccessError(response.StatusCode,
                    response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0")));
            response.EnsureSuccessStatusCode();
            using var releases = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var release = AppUpdates.Select(releases.RootElement, CurrentVersion);
            if (release is null) { installation.LastUpdateCheck = DateTimeOffset.UtcNow; installation.Save(); SetStatus($"현재 버전 {CurrentVersion}이 최신입니다."); return; }
            SetStatus($"{release.Version} 다운로드 중… · 완료 후 자동으로 다시 실행합니다.");
            var stage = Path.Combine(directory, "updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            var sums = await Download(client, release.ChecksumsUrl, 65536, null, timeout.Token);
            if (!AppUpdates.MatchesChecksum(System.Text.Encoding.UTF8.GetString(sums), release.Sha256)) throw new InvalidDataException("릴리스 체크섬을 확인하지 못했습니다.");
            var stagedExe = Path.Combine(stage, "JjogaeStatus.exe");
            await Download(client, release.DownloadUrl, release.Size, stagedExe, timeout.Token);
            if (new FileInfo(stagedExe).Length != release.Size || AppUpdates.Hash(stagedExe) != release.Sha256) throw new InvalidDataException("업데이트 파일 검증에 실패했습니다. 기존 앱을 유지합니다.");
            var fileVersion = FileVersionInfo.GetVersionInfo(stagedExe).FileVersion;
            if (!Version.TryParse(fileVersion, out var version) || version.ToString(3) != release.Version.ToString(3)) throw new InvalidDataException("업데이트 버전이 릴리스와 일치하지 않습니다.");
            var helper = Path.Combine(stage, "updater.exe"); File.Copy(Environment.ProcessPath!, helper);
            using var current = Process.GetCurrentProcess();
            var plan = new UpdatePlan(Environment.ProcessPath!, CurrentVersion.ToString(3), release.Version.ToString(3), release.Sha256, current.Id, current.StartTime.ToUniversalTime().Ticks, Guid.NewGuid().ToString("N"));
            var planPath = Path.Combine(stage, "plan.json"); File.WriteAllText(planPath, JsonSerializer.Serialize(plan, StateStore.Json));
            SetStatus($"{release.Version} 설치 중… · 앱을 다시 실행합니다.");
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(planPath);
            _ = Process.Start(start) ?? throw new InvalidOperationException("업데이트 설치를 시작하지 못했습니다.");
            shutdown();
        }
        catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested) SetStatus("업데이트 확인 시간이 초과됐습니다. 다시 시도해 주세요."); }
        catch (Exception error) { SetStatus(error is InvalidOperationException or InvalidDataException ? error.Message : "업데이트를 완료하지 못했습니다. 기존 앱을 유지하며 다음에 다시 확인합니다."); }
        finally { Busy = false; Changed?.Invoke(); }
    }
    internal static async Task<byte[]> Download(HttpClient client, string url, long limit, string? destination, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("업데이트 파일 크기가 올바르지 않습니다.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellation);
        await using Stream output = destination is null ? new MemoryStream() : new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        var buffer = new byte[65536]; long length = 0; int read;
        while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
        {
            length += read; if (length > limit) throw new InvalidDataException("업데이트 파일 크기가 올바르지 않습니다.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
        }
        return output is MemoryStream memory ? memory.ToArray() : [];
    }
    private void SetStatus(string status) { Status = status; Changed?.Invoke(); }
    public void Dispose() { timer.Stop(); lifetime.Cancel(); lifetime.Dispose(); }
}
