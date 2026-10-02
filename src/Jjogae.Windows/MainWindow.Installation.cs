namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private readonly AppInstallation installation;
    private readonly WindowsUpdater updater;
    private TextBlock? updateStatus, updateLastCheck;
    private Button? updateCheck;
    private void UpdateInstallationStatus()
    {
        if (updateStatus is not null) updateStatus.Text = updater.Status;
        if (updateLastCheck is not null) updateLastCheck.Text = installation.LastUpdateCheck is { } at ? "마지막 확인 · " + Channel.DateText(at) : "아직 확인하지 않았습니다.";
        if (updateCheck is not null) updateCheck.IsEnabled = !updater.Busy;
    }
    private void StartupQuestion()
    {
        if (app.IsTest || installation.StartupChoiceMade) return;
        content.Children.Add(Card(Column(Text("Windows 시작 시 쪼개 상황실을 실행할까요?", 17, true),
            Text("로그인할 때 백그라운드로 실행해 알림을 받습니다. 설정에서 언제든 바꿀 수 있습니다.", 12, secondary: true),
            Row(Button("시작프로그램 등록", () => ChooseStartup(true)), Button("등록하지 않음", () => ChooseStartup(false))))));
    }
    private void ChooseStartup(bool enabled)
    {
        try { installation.SetStartup(enabled); }
        catch (Exception error) { MessageBox.Show(this, error is InvalidOperationException ? error.Message : "시작프로그램 설정을 저장하지 못했습니다. 다시 시도해 주세요.", Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { Render(); }
    }
    private void InstallationSettings()
    {
        var startup = new CheckBox { Content = "Windows 로그인 시 실행", IsChecked = !app.IsTest && installation.Startup.Enabled, Foreground = ink, Margin = new Thickness(0, 8, 0, 8) };
        startup.Click += (_, _) => { if (!app.IsTest) ChooseStartup(startup.IsChecked == true); };
        content.Children.Add(Card(Column(Text("시작프로그램", 17, true), startup,
            Text(app.IsTest ? "시작프로그램에 등록되어 있지 않습니다." : installation.StartupError ?? installation.Startup.Status, 12, secondary: true),
            Button("Windows 시작프로그램 설정 열기", () => { if (!app.IsTest) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }))));
        var automatic = new CheckBox { Content = "자동 업데이트 · 1주일마다 확인", IsChecked = installation.AutomaticUpdates, Foreground = ink, Margin = new Thickness(0, 8, 0, 8) };
        automatic.Click += (_, _) => { installation.AutomaticUpdates = automatic.IsChecked == true; if (!app.IsTest) installation.Save(); };
        updateStatus = Text(updater.Status, 12, secondary: true); updateStatus.TextWrapping = TextWrapping.Wrap;
        updateLastCheck = Text("", 11, secondary: true);
        updateCheck = Button("업데이트 확인", async () => { if (!app.IsTest) await updater.Check(true); });
        content.Children.Add(Card(Column(Text("앱 업데이트", 17, true), Text("현재 버전 " + WindowsUpdater.CurrentVersion, 13), automatic,
            updateCheck, updateStatus, updateLastCheck)));
        UpdateInstallationStatus();
    }
}
