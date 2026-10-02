using System.Net;
using System.Net.Http;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private async Task VerifyPublicUpdates(Action<bool, string> check)
    {
        check(PublicUpdateConnection.AccessError(HttpStatusCode.NotFound).Contains("공개"), "updates / unpublished public release reports unavailability");
        check(PublicUpdateConnection.AccessError(HttpStatusCode.Forbidden, true).Contains("한도"), "updates / rate limiting supports retry without requesting login");
        check(!PublicUpdateConnection.AccessError(HttpStatusCode.Unauthorized).Contains("로그인"), "updates / public download errors never request GitHub login");
        var requests = new List<HttpRequestMessage>();
        var attempt = 0;
        using var client = new HttpClient(new PublicDownloadFixture(request =>
        {
            requests.Add(request); attempt++;
            return new HttpResponseMessage(attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new ByteArrayContent([1, 2, 3, 4]) };
        }));
        var url = AppUpdates.DownloadUrl(new Version(0, 4, 15), "JjogaeStatus.exe");
        var failed = false;
        try { await WindowsUpdater.Download(client, url, 4, null, CancellationToken.None); }
        catch (HttpRequestException) { failed = true; }
        check(failed, "updates / unavailable asset fails before installation");
        var bytes = await WindowsUpdater.Download(client, url, 4, null, CancellationToken.None);
        check(bytes.SequenceEqual(new byte[] { 1, 2, 3, 4 }), "updates / retry succeeds after download failure");
        check(requests.All(request => request.Headers.Authorization is null && request.RequestUri?.AbsoluteUri == url),
            "updates / versioned public downloads never send account credentials");
        var oversized = false;
        try { await WindowsUpdater.Download(client, url, 3, null, CancellationToken.None); }
        catch (InvalidDataException) { oversized = true; }
        check(oversized, "updates / oversized download is rejected");
    }

    private sealed class PublicDownloadFixture(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
