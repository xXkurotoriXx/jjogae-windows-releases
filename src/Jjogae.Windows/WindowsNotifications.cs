using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;
using Notification = Jjogae.Core.Notification;

namespace Jjogae.Windows;

internal sealed class WindowsNotifications
{
    public string Status { get; private set; } = "Windows 알림 준비 중";
    public event Action? Changed;
    public event Action<Notification>? Failed;
    private Task? initialization;
    public Task Initialize() => initialization ??= InitializeCore();
    private async Task InitializeCore()
    {
        try
        {
            NotificationRegistration.EnsureShortcut();
            // The Shell indexes a new Start menu sender asynchronously. Register
            // before polling starts rather than racing the first real alert.
            await Task.Delay(2500);
            SetStatus("Windows 알림 준비 완료");
        }
        catch (Exception error) { SetStatus($"Windows 알림 등록 실패 (0x{error.HResult:X8})"); }
    }

    internal static ToastContent Content(Notification entry) => new ToastContentBuilder()
        .SetToastDuration(entry.Kind == "알림 테스트" ? ToastDuration.Long : ToastDuration.Short)
        .AddArgument("section", entry.Source == "카페" ? "cafe" : "home")
        .AddText($"{entry.Source} · {entry.Kind}")
        .AddText(entry.Title)
        .AddText(Channel.DateText(entry.At))
        .GetToastContent();

    public bool Send(Notification entry)
    {
        try
        {
            NotificationRegistration.EnsureShortcut();
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            if (notifier.Setting != NotificationSetting.Enabled)
            {
                SetStatus("Windows에서 알림이 차단되어 있습니다. 시스템 알림 설정을 확인해 주세요.");
                return false;
            }
            var toast = new ToastNotification(Content(entry).GetXml())
            {
                Tag = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(entry.Id)))[..16],
                Group = "jjogae", ExpirationTime = DateTimeOffset.Now.AddDays(1), SuppressPopup = false
            };
            toast.Failed += (_, args) => Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                SetStatus($"Windows 알림 전달 실패 (0x{args.ErrorCode.HResult:X8}) · 다시 시도합니다.");
                Failed?.Invoke(entry);
            }));
            notifier.Show(toast);
            SetStatus("Windows에 전달했습니다. 배너가 없다면 방해 금지·알림 센터를 확인해 주세요.");
            return true;
        }
        catch (Exception error)
        {
            SetStatus($"Windows 알림 전달 실패 (0x{error.HResult:X8}) · 다시 시도합니다.");
            return false;
        }
    }
    internal void RemoveCafe(string id)
    {
        try
        {
            var tag = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("cafe:" + id)))[..16];
            ToastNotificationManagerCompat.History.Remove(tag, "jjogae");
        }
        catch (Exception) { SetStatus("삭제된 공지의 Windows 알림을 정리하지 못했습니다. 알림 센터에서 지워 주세요."); }
    }
    private void SetStatus(string value) { Status = value; Changed?.Invoke(); }
    // Explicit local diagnostic only: normal CI smoke tests never register or send real toasts.
    internal static async Task<string> VerifyNativeDelivery()
    {
        var sender = new WindowsNotifications(); var failed = false;
        sender.Failed += _ => failed = true;
        var entry = new Notification("test:native", "쪼개 상황실", "알림 테스트", "Windows 알림 전달 확인 🫧", DateTimeOffset.Now, DateTimeOffset.Now);
        try
        {
            await sender.Initialize();
            if (!sender.Send(entry)) return "FAIL: " + sender.Status;
            await Task.Delay(30000);
            var history = ToastNotificationManagerCompat.History.GetHistory();
            return !failed && history.Any(x => x.Group == "jjogae" && x.Content.InnerText.Contains(entry.Title))
                ? "PASS: Windows accepted the native toast and retained its emoji text in Notification Center."
                : "FAIL: Windows notification was not found in Notification Center. " + sender.Status;
        }
        finally
        {
            var tag = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(entry.Id)))[..16];
            ToastNotificationManagerCompat.History.Remove(tag, "jjogae");
        }
    }
}
