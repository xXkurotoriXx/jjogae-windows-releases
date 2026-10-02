namespace Jjogae.Core;

// Independent refreshes must not drain several banners in the same instant.
public sealed class NotificationDelivery
{
    private DateTimeOffset nextAttempt;
    public void Drain(AppState state, DateTimeOffset now, Func<Notification, bool> send)
    {
        if (!state.Settings.Notifications || now < nextAttempt) return;
        state.Pending.RemoveAll(x => x.DetectedAt < now.AddDays(-1));
        if (state.Pending.FirstOrDefault() is not { } entry) return;
        nextAttempt = now.AddMinutes(1);
        if (!send(entry)) return;
        state.Pending.Remove(entry);
        nextAttempt = now.AddSeconds(15);
    }
}
