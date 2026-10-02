using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Jjogae.Windows;

public sealed class YouTubeWindow : Window
{
    private readonly WebView2 web = new();
    private readonly string profile;
    private readonly Action<YouTubeWebObservation> observed;
    private readonly TextBlock status = new() { VerticalAlignment = VerticalAlignment.Center, Text = "YouTube 로그인 후 정보 확인을 눌러 주세요." };
    private Task? initialization;
    private CancellationTokenSource? reading;
    private readonly System.Windows.Threading.DispatcherTimer loginPoll = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool closing, signingOut;
    private ulong? navigation;
    private ulong? blockedNavigation;
    private readonly List<Window> authenticationWindows = [];
    private DateTimeOffset lastRefresh;

    private readonly Action<CoreWebView2>? configureTest;
    public YouTubeWindow(string directory, Action<YouTubeWebObservation> onObservation, Action<CoreWebView2>? configureTest = null)
    {
        profile = Path.Combine(directory, "YouTubeWebView2"); observed = onObservation; this.configureTest = configureTest;
        Title = "YouTube — 쪼개 상황실"; Width = 1040; Height = 780; MinWidth = 720; MinHeight = 500;
        var root = new DockPanel(); var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
        void Add(string title, Action action)
        {
            var button = new Button { Content = title, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
            button.Click += (_, _) => action(); bar.Children.Add(button);
        }
        Add("루파 멤버십", () => Navigate(YouTubeWebPolicy.Membership));
        Add("루파 채널", () => Navigate(Channel.YouTubeUrl));
        Add("멤버십 관리", () => Navigate(YouTubeWebPolicy.PaidMemberships));
        Add("정보 확인", () => _ = ReadPage()); bar.Children.Add(status);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar); root.Children.Add(web); Content = root;
        Loaded += async (_, _) => await Initialize();
        loginPoll.Tick += async (_, _) => { if (IsVisible && reading is null && !signingOut && !closing) await ReadPage(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) loginPoll.Start(); else loginPoll.Stop(); };
        Closing += (_, e) => { if (!closing) { e.Cancel = true; Hide(); } };
    }
    private Task Initialize() => initialization ??= InitializeCore();
    private async Task InitializeCore()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            if (closing) return;
            await web.EnsureCoreWebView2Async(environment);
            if (closing) return;
            var core = web.CoreWebView2;
            configureTest?.Invoke(core);
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false; core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += OpenAuthenticationWindow;
            core.NavigationStarting += (_, e) =>
            {
                CancelRead();
                if (closing || signingOut || !YouTubeWebPolicy.Allows(e.Uri)) { e.Cancel = true; blockedNavigation = e.NavigationId; ShowBlockedNavigation(e.Uri); return; }
                blockedNavigation = null;
                navigation = e.NavigationId;
                status.Text = "페이지를 여는 중";
            };
            core.NavigationCompleted += async (_, e) =>
            {
                if (signingOut || closing || navigation != e.NavigationId) return;
                navigation = null;
                if (blockedNavigation == e.NavigationId) return;
                if (e.IsSuccess) await ReadPage();
                else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    status.Text = $"페이지 이동 실패: {e.WebErrorStatus} · HTTP {e.HttpStatusCode}";
            };
            core.SourceChanged += async (_, e) => { if (!e.IsNewDocument && !signingOut && !closing) await ReadPage(); };
            Navigate(Channel.YouTubeUrl);
        }
        catch (Exception) { if (!closing) status.Text = "WebView2를 시작하지 못했습니다. 창을 닫고 앱을 다시 실행해 주세요."; }
    }
    private void ShowBlockedNavigation(string address)
    {
        if (!closing && !signingOut)
            status.Text = Uri.TryCreate(address, UriKind.Absolute, out var uri)
                ? $"허용되지 않은 이동 주소: {uri.Host}" : "이동 주소를 확인할 수 없습니다.";
    }
    private async void OpenAuthenticationWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (closing || signingOut || sender is not CoreWebView2 opener) return;
        if (e.Uri != "about:blank" && !YouTubeWebPolicy.Allows(e.Uri)) { ShowBlockedNavigation(e.Uri); return; }
        using var deferral = e.GetDeferral();
        var childWeb = new WebView2();
        var child = new Window { Title = "YouTube 로그인", Width = 720, Height = 780, Content = childWeb, Owner = this };
        authenticationWindows.Add(child);
        CancelRead();
        var childClosed = false;
        child.Closed += (_, _) => { childClosed = true; authenticationWindows.Remove(child); childWeb.Dispose(); };
        try
        {
            child.Show();
            await childWeb.EnsureCoreWebView2Async(opener.Environment);
            if (closing || signingOut || childClosed) return;
            var core = childWeb.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false; core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false; core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.NavigationStarting += (_, args) =>
            {
                if (closing || signingOut || args.Uri != "about:blank" && !YouTubeWebPolicy.Allows(args.Uri))
                { args.Cancel = true; ShowBlockedNavigation(args.Uri); }
            };
            core.NewWindowRequested += OpenAuthenticationWindow;
            core.WindowCloseRequested += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (!childClosed) child.Close(); }));
            configureTest?.Invoke(core);
            e.NewWindow = core;
        }
        catch (Exception)
        {
            if (!childClosed) child.Close();
            if (!closing && !signingOut) status.Text = "인증 창을 열지 못했습니다. 다시 로그인해 주세요.";
        }
    }
    private void CloseAuthenticationWindows()
    {
        foreach (var child in authenticationWindows.ToArray()) child.Close();
    }
    private void Navigate(string address)
    {
        if (!closing && !signingOut && YouTubeWebPolicy.Allows(address) && web.CoreWebView2 is { } core) core.Navigate(address);
    }
    public async Task Refresh(bool force = false)
    {
        if (closing || signingOut || !force && DateTimeOffset.UtcNow - lastRefresh < TimeSpan.FromHours(6)) return;
        lastRefresh = DateTimeOffset.UtcNow;
        await Initialize();
        if (closing || signingOut) return;
        if (IsVisible) await ReadPage(); else Navigate(Channel.YouTubeUrl);
    }
    private void CancelRead() { reading?.Cancel(); reading?.Dispose(); reading = null; }
    private async Task ReadPage()
    {
        CancelRead();
        if (closing || signingOut || navigation is not null || authenticationWindows.Count > 0 || web.CoreWebView2 is not { } core || !YouTubeWebPolicy.CanObserve(core.Source)) return;
        var cancellation = new CancellationTokenSource(); reading = cancellation; var token = cancellation.Token;
        var address = core.Source;
        YouTubeWebObservation? lastValue = null;
        try
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (attempt > 0) await Task.Delay(2000, token);
                token.ThrowIfCancellationRequested();
                if (core.Source != address) return;
                if (new Uri(address).AbsolutePath == "/paid_memberships") await core.ExecuteScriptAsync(YouTubeWebScripts.ExpandDetails);
                var json = await core.ExecuteScriptAsync(YouTubeWebScripts.Read);
                token.ThrowIfCancellationRequested();
                if (core.Source != address || json.Length > 8192) return;
                using var document = JsonDocument.Parse(json);
                var value = YouTubeWebObservation.Decode(document.RootElement, DateTimeOffset.UtcNow);
                if (value is null) continue;
                if (lastValue is null || lastValue with { CheckedAt = value.CheckedAt } != value) { observed(value); lastValue = value; }
                status.Text = value.Subscribed is null ? "로그인 확인 · 구독 정보를 확인하는 중" : "루파 구독 정보 반영 완료";
                var currentPath = new Uri(address).AbsolutePath.TrimEnd('/');
                if (!IsVisible && currentPath == "") { Navigate(Channel.YouTubeUrl); return; }
                if (!IsVisible && currentPath != "/paid_memberships" && !currentPath.EndsWith("/membership", StringComparison.Ordinal) && value.Subscribed is not null)
                { Navigate(YouTubeWebPolicy.Membership); return; }
                if (value.MembershipActive == false && attempt < 2) continue;
                if (value.MembershipMonths is not null || value.NextBillingLabel is not null || value.MembershipActive == false)
                {
                    if (!IsVisible && new Uri(address).AbsolutePath != "/paid_memberships") Navigate(YouTubeWebPolicy.PaidMemberships);
                    return;
                }
            }
            status.Text = lastValue is null ? "로그인 상태를 확인해 주세요." : "로그인 확인 · 아직 확인되지 않은 정보가 있습니다.";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closing && !token.IsCancellationRequested) status.Text = "정보를 읽지 못했습니다. 저장된 정보는 유지합니다."; }
        finally { if (ReferenceEquals(reading, cancellation)) { reading = null; cancellation.Dispose(); } }
    }
    public async Task SignOut()
    {
        signingOut = true; CancelRead(); CloseAuthenticationWindows();
        try
        {
            await Initialize();
            if (web.CoreWebView2 is not { } core) throw new InvalidOperationException("저장된 YouTube 로그인 세션을 지우지 못했습니다.");
            core.Stop(); core.CookieManager.DeleteAllCookies(); await core.Profile.ClearBrowsingDataAsync();
        }
        catch { signingOut = false; throw; }
    }
    public void ResumeReading() { signingOut = false; lastRefresh = DateTimeOffset.MinValue; Navigate(Channel.YouTubeUrl); }
    public void ShutDown() { closing = true; loginPoll.Stop(); CancelRead(); CloseAuthenticationWindows(); Close(); web.Dispose(); }
}
