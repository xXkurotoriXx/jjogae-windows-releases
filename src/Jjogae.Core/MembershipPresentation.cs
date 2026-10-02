namespace Jjogae.Core;

public static class MembershipPresentation
{
    public static bool Visible(AppState state) => !state.Settings.YouTubeMembershipHidden && (state.YouTubeWeb?.MembershipActive == true || state.Settings.YouTubeMemberActive);
    public static string Value(AppState state)
    {
        if (state.YouTubeWeb?.MembershipMonths is { } months) return $"{months + state.Settings.YouTubeAdditionalMonths}개월";
        if (state.Settings.YouTubeMemberSince is not { } since || !state.Settings.YouTubeMemberActive) return "이용 중";
        return state.Settings.YouTubeAdditionalMonths > 0 ? Cumulative(since, Channel.Today, state.Settings.YouTubeAdditionalMonths) : Channel.Elapsed(since);
    }
    public static string Cumulative(DateOnly since, DateOnly today, int additionalMonths)
    {
        if (since > today) return "확인 중";
        var end = today.AddDays(1); // Include the first membership day, as on macOS.
        var months = (end.Year - since.Year) * 12 + end.Month - since.Month;
        if (since.AddMonths(months) > end) months--;
        var days = end.DayNumber - since.AddMonths(months).DayNumber;
        var total = months + Math.Clamp(additionalMonths, 0, 1200);
        return days == 0 ? $"{total}개월" : $"{total}개월 {days}일";
    }
    public static string Detail(AppState state) => string.Join(" · ", new[] {
        state.YouTubeWeb?.Tier,
        state.YouTubeWeb?.NextBillingLabel is { } billing ? "다음 결제 " + billing : null,
        state.Settings.YouTubeMemberActive && state.Settings.YouTubeMemberSince is { } since ? $"직접 입력 · {since:yyyy.MM.dd}부터" : null
    }.Where(x => !string.IsNullOrWhiteSpace(x)));
}
