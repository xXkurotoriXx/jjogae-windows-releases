using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Jjogae.Windows;

// Public release checks never read credentials or invoke Git/GCM.
internal static class PublicUpdateConnection
{
    internal static string AccessError(HttpStatusCode code, bool rateLimited = false) => code switch
    {
        HttpStatusCode.Forbidden when rateLimited => "GitHub 요청 한도에 도달했습니다. 잠시 후 다시 확인해 주세요.",
        HttpStatusCode.NotFound => "공개 업데이트를 아직 사용할 수 없습니다. 잠시 후 다시 확인해 주세요.",
        _ => "공개 업데이트 서버에 연결하지 못했습니다. 잠시 후 다시 확인해 주세요."
    };

    internal static async Task<int> VerifyAccess()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("JjogaeStatus-UpdateCheck/" + WindowsUpdater.CurrentVersion);
            using var response = await client.GetAsync($"https://api.github.com/repos/{AppUpdates.Repository}/releases?per_page=100", timeout.Token);
            response.EnsureSuccessStatusCode();
            using var releases = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var latest = AppUpdates.Select(releases.RootElement, new Version(0, 0, 0));
            Console.WriteLine(JsonSerializer.Serialize(new { authenticated = false, httpStatus = (int)response.StatusCode,
                latestVersion = latest?.Version.ToString(3), readOnly = true }));
            return latest is null ? 1 : 0;
        }
        catch
        {
            Console.WriteLine(JsonSerializer.Serialize(new { authenticated = false, available = false, readOnly = true }));
            return 1;
        }
    }
}
