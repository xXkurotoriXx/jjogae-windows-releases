using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Jjogae.Windows;

public sealed class LoginWindow : Window
{
    private readonly WebView2 web = new();
    private readonly string profile;
    private readonly Func<Task> verify;
    private readonly TextBlock message = new() { Text = "이미 로그인했다면 ‘로그인 확인’을 누르세요.", VerticalAlignment = VerticalAlignment.Center };
    public bool Ready => web.CoreWebView2 is not null;

    public LoginWindow(string directory, Func<Task> onVerify)
    {
        profile = Path.Combine(directory, "WebView2"); verify = onVerify;
        Title = "치지직 · 네이버 로그인 — 쪼개 상황실"; Width = 1020; Height = 760; MinWidth = 700; MinHeight = 500;
        var root = new DockPanel();
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
        foreach (var (label, address) in new[] { ("치지직", Channel.Url), ("카페", Channel.CafeUrl) })
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
            button.Click += (_, _) => { if (Ready) web.CoreWebView2.Navigate(address); }; controls.Children.Add(button);
        }
        var confirm = new Button { Content = "로그인 확인", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 10, 0) };
        confirm.Click += async (_, _) =>
        {
            confirm.IsEnabled = false;
            try { await verify(); message.Text = "확인이 끝났습니다. 메인 창에서 로그인 상태를 확인해 주세요."; }
            catch (Exception error) { message.Text = error.Message; }
            finally { confirm.IsEnabled = true; }
        };
        controls.Children.Add(confirm); controls.Children.Add(message); DockPanel.SetDock(controls, Dock.Top); root.Children.Add(controls); root.Children.Add(web); Content = root;
        Loaded += async (_, _) => await Initialize();
        Closing += (_, e) => { if (!allowClose) { e.Cancel = true; Hide(); } };
    }

    private bool allowClose;
    private Task<bool>? initialization;
    private CoreWebView2Controller? background;
    public void ShutDown() { allowClose = true; Close(); background?.Close(); background = null; web.Dispose(); }
    private static bool Allowed(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort
        && new[] { "naver.com", "naver.net", "pstatic.net" }.Any(host => uri.Host == host || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    public Task<bool> Initialize() => initialization ??= InitializeCore();
    // WPF's WebView2 waits for a visible visual tree. A separate, invisible
    // controller can restore this app's cookie profile without opening a login window.
    internal async Task<bool> RestoreSession(Action<CoreWebView2>? configureTest = null)
    {
        if (Ready || background is not null) return true;
        var environment = await CoreWebView2Environment.CreateAsync(null, profile);
        if (allowClose) return false;
        var controller = await environment.CreateCoreWebView2ControllerAsync(new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle);
        if (allowClose) { controller.Close(); return false; }
        background = controller; background.IsVisible = false;
        background.CoreWebView2.Settings.AreDevToolsEnabled = false;
        background.CoreWebView2.Settings.AreHostObjectsAllowed = false;
        background.CoreWebView2.Settings.IsWebMessageEnabled = false;
        background.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        background.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
        background.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        background.CoreWebView2.NavigationStarting += (_, e) => e.Cancel = true;
        background.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;
        background.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
        configureTest?.Invoke(background.CoreWebView2);
        return true;
    }
    private async Task<bool> InitializeCore()
    {
        if (Ready) return true;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            if (allowClose) return false;
            await web.EnsureCoreWebView2Async(environment);
            if (allowClose) return false;
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            web.CoreWebView2.Settings.IsWebMessageEnabled = false;
            web.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
            web.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
            web.CoreWebView2.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            web.CoreWebView2.NavigationStarting += (_, e) => { if (!Allowed(e.Uri)) e.Cancel = true; };
            web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; if (Allowed(e.Uri)) web.CoreWebView2.Navigate(e.Uri); };
            web.CoreWebView2.DownloadStarting += (_, e) => e.Cancel = true;
            web.CoreWebView2.Navigate(Channel.Url);
            return true;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            message.Text = "Microsoft Edge WebView2 Runtime이 필요합니다. 설치 후 로그인 창을 다시 열어 주세요.";
            if (IsVisible && MessageBox.Show("로그인 창에 Microsoft Edge WebView2 Runtime이 필요합니다. Microsoft 공식 다운로드 페이지를 열까요?", "WebView2 Runtime", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                AppController.OpenUrl("https://developer.microsoft.com/microsoft-edge/webview2/");
            return false;
        }
    }

    public async Task<string> Cookies(Uri uri)
    {
        var core = web.CoreWebView2 ?? background?.CoreWebView2;
        if (core is null || !Allowed(uri.ToString())) return "";
        var cookies = await core.CookieManager.GetCookiesAsync(uri.ToString());
        return string.Join("; ", cookies.Select(cookie => cookie.Name + "=" + cookie.Value));
    }
    public async Task SignOut()
    {
        if (!await RestoreSession()) return;
        var core = web.CoreWebView2 ?? background!.CoreWebView2;
        core.CookieManager.DeleteAllCookies();
        await core.Profile.ClearBrowsingDataAsync();
        if (Ready) web.CoreWebView2!.Navigate(Channel.Url);
    }
}
