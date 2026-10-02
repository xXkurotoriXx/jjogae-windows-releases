using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private Popup? accountPopup;
    private string? displayedUserId;
    private void UpdateAccountChip()
    {
        if (loginButton is null) return;
        if (displayedUserId != app.State.Account?.UserId || app.State.Account is null) { if (accountPopup is not null) accountPopup.IsOpen = false; displayedUserId = app.State.Account?.UserId; }
        if (app.State.Account is not { } account) { loginButton.Content = "로그인"; loginButton.ToolTip = "네이버 로그인"; return; }
        var chip = new Grid(); chip.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); chip.ColumnDefinitions.Add(new ColumnDefinition());
        var badges = new StackPanel { Orientation = Orientation.Horizontal };
        badges.Children.Add(new Border { Width = 6, Height = 6, Background = Brush("#30BC88"), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
        if (!compactAccountChip && app.State.ChatProfile is { } profile && profile.BelongsTo(account))
            foreach (var badge in profile.Badges.Take(3)) badges.Children.Add(ChatBadgeImage(badge, 16));
        chip.Children.Add(badges);
        var name = Text(account.Nickname, 12, true); name.Tag = "account-name"; name.MaxWidth = compactAccountChip ? 80 : 100; name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis; name.Margin = new Thickness(4, 0, 0, 0); Grid.SetColumn(name, 1); chip.Children.Add(name);
        loginButton.Content = chip; loginButton.ToolTip = account.Nickname + " · 채팅 프로필";
        System.Windows.Automation.AutomationProperties.SetName(loginButton, account.Nickname + " 채팅 프로필");
    }
    private UIElement ChatBadgeImage(ChatBadge badge, double size)
    {
        var image = new Image { Width = size, Height = size, Margin = new Thickness(0, 0, 4, 0), Stretch = Stretch.Uniform,
            ToolTip = badge.Title + (badge.Detail.Length == 0 ? "" : " · " + badge.Detail) };
        System.Windows.Automation.AutomationProperties.SetName(image, badge.Title);
        if (app.IsTest) image.Source = Icon;
        else if (ChatProfileParser.SafeImage(badge.ImageUrl))
        {
            _ = SetDisplayImage(image, badge.ImageUrl);
        }
        return image;
    }
    private void ShowChatProfile()
    {
        if (app.State.Account is not { } account) return;
        if (accountPopup?.IsOpen == true) { accountPopup.IsOpen = false; return; }
        var details = Column(Text(account.Nickname, 16, true), Text(Channel.Name + " 채널", 11, secondary: true));
        if (app.State.ChatProfile is { } profile && profile.BelongsTo(account))
        {
            var badges = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) };
            foreach (var badge in profile.Badges) badges.Children.Add(ChatBadgeImage(badge, 28));
            details.Children.Add(badges);
            if (profile.SubscriptionMonths is { } months) details.Children.Add(StatisticRow("구독", $"{months:N0}개월"));
            if (profile.SubscriptionTier.Length > 0) details.Children.Add(Text(profile.SubscriptionTier, 12, secondary: true));
            if (profile.ContinuousDonationDays is > 0) details.Children.Add(StatisticRow("연속 후원", $"{profile.ContinuousDonationDays:N0}일"));
            if (profile.FollowedAt is { } followed) details.Children.Add(StatisticRow("팔로우", followed.ToString("yyyy.MM.dd")));
            details.Children.Add(Text(profile.RefreshError.Length > 0 ? profile.RefreshError : Channel.DateText(profile.CheckedAt) + " 확인", 11, secondary: true));
        }
        else
        {
            details.Children.Add(Text("채팅 프로필을 아직 확인하지 못했습니다.", 12, secondary: true));
            var guidance = Text("브라우저에서\n‘로그인 확인’을 눌러 주세요.", 11, secondary: true);
            guidance.TextWrapping = TextWrapping.Wrap;
            details.Children.Add(guidance);
            details.Children.Add(Button("로그인 브라우저 열기", () => { if (accountPopup is not null) accountPopup.IsOpen = false; app.ShowLogin(this); }));
        }
        details.Children.Add(Row(Button("계정 설정", () => { if (accountPopup is not null) accountPopup.IsOpen = false; Select(4); }), Button("닫기", () => { if (accountPopup is not null) accountPopup.IsOpen = false; })));
        var container = Card(details); container.Width = 280; container.Margin = new Thickness(0, 6, 0, 0);
        container.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : Brush(dark ? "#252C40" : "#F1F3FA");
        accountPopup = new Popup { Child = container, PlacementTarget = loginButton, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        accountPopup.IsOpen = true;
    }
}
