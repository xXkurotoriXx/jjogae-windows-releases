using System.Windows.Threading;

namespace Jjogae.Windows;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--update-check")) return PublicUpdateConnection.VerifyAccess().GetAwaiter().GetResult();
        if (args.Length == 2 && args[0] == "--apply-update") return UpdateInstaller.Run(args[1]).GetAwaiter().GetResult();
        var smoke = args.Contains("--smoke-test") || args.Contains("--parity-test") || args.Contains("--notification-test");
        var preview = args.Contains("--preview");
        using var singleton = new Mutex(true, smoke || preview ? "Local\\JjogaeWindows.QA." + Guid.NewGuid().ToString("N") : "Local\\JjogaeWindows.PrivateTest.v1", out var created);
        if (!created) { MessageBox.Show("쪼개 상황실이 이미 실행 중입니다. 작업 표시줄에서 열어 주세요.", "쪼개 상황실"); return 0; }
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            MessageBox.Show("작업을 마치지 못했습니다. 저장된 기록은 삭제하지 않습니다.\n" + e.Exception.Message, "쪼개 상황실", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        try
        {
            if ((!smoke && !preview) || args.Contains("--notification-test")) NotificationRegistration.SetProcessIdentity(args.Contains("--notification-test"));
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JjogaeWindows");
            if (smoke || preview) root = Path.Combine(Path.GetTempPath(), "JjogaeWindows-QA-" + Guid.NewGuid().ToString("N"));
            using var controller = new AppController(root, smoke || preview);
            var window = new MainWindow(controller);
            if (args.Contains("--startup")) window.Loaded += (_, _) => window.WindowState = WindowState.Minimized;
            if (args.Contains("--notification-test"))
            {
                window.Title = "쪼개 상황실 · 알림 표시 검사";
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Width = 600; window.Height = 540;
                window.Left = SystemParameters.WorkArea.Right - window.Width;
                window.Top = SystemParameters.WorkArea.Bottom - window.Height;
            }
            if (preview) window.Loaded += (_, _) => window.PreparePreview();
            if (smoke)
            {
                window.Loaded += async (_, _) =>
                {
                    await Task.Delay(1000);
                    try
                    {
                        var screenshots = args.SkipWhile(x => x != "--screenshots").Skip(1).FirstOrDefault();
                        var report = args.Contains("--notification-test") ? await WindowsNotifications.VerifyNativeDelivery()
                            : args.Contains("--parity-test") ? await window.VerifyMacParity(screenshots) : await window.VerifyCleanLaunch(screenshots);
                        var destination = args.SkipWhile(x => x != "--report").Skip(1).FirstOrDefault();
                        if (destination is not null) File.WriteAllText(destination, report);
                        app.Shutdown(report.Contains("FAIL") ? 1 : 0);
                    }
                    catch (Exception error)
                    {
                        var destination = args.SkipWhile(x => x != "--report").Skip(1).FirstOrDefault();
                        if (destination is not null) File.WriteAllText(destination, error.ToString());
                        app.Shutdown(1);
                    }
                };
            }
            return app.Run(window);
        }
        catch (Exception error)
        {
            MessageBox.Show("실행을 중단했습니다. 기존 저장 파일은 변경하지 않았습니다.\n" + error.Message, "쪼개 상황실", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
}
