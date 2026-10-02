using Microsoft.Win32;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private void VerifyStartupRegistration(Action<bool, string> check)
    {
        var directory = Path.Combine(app.Store.DirectoryPath, "시작프로그램 검증 폴더");
        var registry = @"Software\JjogaeWindows\QA\" + Guid.NewGuid().ToString("N");
        var runKey = registry + @"\Run"; var approvalKey = registry + @"\StartupApproved";
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "JjogaeStatus.exe");
        File.WriteAllText(executable, "test target; never executed");
        var startup = new WindowsStartup(Path.Combine(directory, "Startup"), executable, runKey, approvalKey);
        var fixture = new AppInstallation(Path.Combine(directory, "Profile"), startup);
        try
        {
            check(!startup.Registered && !startup.Enabled, "startup / isolated clean profile is not registered");
            fixture.SetStartup(true);
            check(startup.Registered && startup.Enabled && File.Exists(startup.ShortcutPath), "startup / enable creates a real Windows shell shortcut");
            var link = startup.ReadShortcut();
            check(link?.Target == executable && link?.Arguments == "--startup" && link?.Directory == directory,
                "startup / shell independently reads Unicode target, background argument and working directory");
            var restored = new AppInstallation(Path.Combine(directory, "Profile"), startup); restored.Load();
            check(restored.StartupChoiceMade && restored.Startup.Enabled, "startup / choice is saved after verified registration and survives reload");
            fixture.SetStartup(true);
            check(Directory.GetFiles(Path.GetDirectoryName(startup.ShortcutPath)!, "*.lnk").Length == 1, "startup / repeated enable creates one logon shortcut without temporary links");
            using (var key = Registry.CurrentUser.CreateSubKey(approvalKey + @"\StartupFolder")) key.SetValue(WindowsStartup.ShortcutName, new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
            check(startup.Registered && !startup.Enabled && startup.DisabledByWindows && startup.Status.Contains("사용 안 함"), "startup / Windows disabled state is displayed instead of false success");
            fixture.SetStartup(true);
            check(!startup.Enabled && startup.DisabledByWindows, "startup / registration preserves a Windows-level disabled choice");
            using (var key = Registry.CurrentUser.OpenSubKey(approvalKey + @"\StartupFolder", true)) key!.DeleteValue(WindowsStartup.ShortcutName);
            check(startup.Enabled, "startup / Windows re-enable is observed without changing saved app choice");
            fixture.SetStartup(false);
            check(!File.Exists(startup.ShortcutPath) && !startup.Registered && !startup.Enabled, "startup / disable actually removes the logon shortcut");
            restored.Load(); check(restored.StartupChoiceMade && !restored.Startup.Enabled, "startup / opting out remains remembered without prompting again");
            using (var key = Registry.CurrentUser.CreateSubKey(runKey)) { key.SetValue("JjogaeStatus", AppInstallation.StartupCommand(executable)); key.SetValue("AnotherApp", "keep this unrelated entry"); }
            startup.MigrateLegacy();
            check(startup.Enabled && !startup.LegacyRegistered, "startup / legacy Run entry migrates to verified shortcut without duplicate launches");
            using (var key = Registry.CurrentUser.OpenSubKey(runKey)) check(key!.GetValue("AnotherApp") as string == "keep this unrelated entry", "startup / migration preserves other startup applications");
            startup.MigrateLegacy(); check(startup.Enabled, "startup / migration remains stable across subsequent launches");
            fixture.SetStartup(false);
            using (var key = Registry.CurrentUser.CreateSubKey(runKey)) key.SetValue("JjogaeStatus", AppInstallation.StartupCommand(executable));
            using (var key = Registry.CurrentUser.CreateSubKey(approvalKey + @"\Run")) key.SetValue("JjogaeStatus", new byte[] { 3, 0, 0, 0 }, RegistryValueKind.Binary);
            startup.MigrateLegacy();
            check(startup.LegacyRegistered && !startup.Registered && startup.DisabledByWindows, "startup / migration does not bypass Windows disabling the legacy entry");
            fixture.SetStartup(false);
            check(!startup.LegacyRegistered && !File.Exists(startup.ShortcutPath), "startup / explicit disable removes both legacy and shortcut registrations");
            var missing = new AppInstallation(Path.Combine(directory, "MissingProfile"), new WindowsStartup(Path.Combine(directory, "MissingStartup"), executable + ".missing", runKey, approvalKey));
            var failed = false;
            try { missing.SetStartup(true); } catch (InvalidOperationException) { failed = true; }
            check(failed && !missing.StartupChoiceMade && !File.Exists(Path.Combine(directory, "MissingProfile", "installation.json")), "startup / missing executable cannot dismiss first-launch choice as successful");
            var blockedFolder = Path.Combine(directory, "NotAFolder"); File.WriteAllText(blockedFolder, "blocked");
            var blocked = new AppInstallation(Path.Combine(directory, "BlockedProfile"), new WindowsStartup(blockedFolder, executable, runKey, approvalKey));
            failed = false;
            try { blocked.SetStartup(true); } catch (IOException) { failed = true; }
            check(failed && !blocked.StartupChoiceMade, "startup / shortcut write failure leaves the registration choice retryable");
            fixture.SetStartup(true); File.Delete(executable);
            check(!startup.Registered && !startup.Enabled, "startup / missing or moved target is not reported as enabled");
            File.WriteAllText(executable, "test target; never executed");
            var relocated = Path.Combine(directory, "다른 경로"); Directory.CreateDirectory(relocated);
            var newTarget = Path.Combine(relocated, "JjogaeStatus.exe"); File.WriteAllText(newTarget, "test target; never executed");
            var moved = new WindowsStartup(Path.GetDirectoryName(startup.ShortcutPath)!, newTarget, runKey, approvalKey);
            check(!moved.Registered, "startup / a different executable path requires registration repair");
            moved.SetEnabled(true);
            check(moved.Enabled && moved.ReadShortcut()?.Target == newTarget, "startup / registering from a new location replaces the stale target");
            moved.SetEnabled(false);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(registry, false); }
    }
}
