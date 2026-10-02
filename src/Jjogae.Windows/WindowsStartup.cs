using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Jjogae.Windows;

// A per-user Startup shortcut works for this unpackaged desktop application without elevation.
internal sealed class WindowsStartup(string folder, string executable,
    string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run",
    string approvalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved")
{
    internal const string ShortcutName = "쪼개 상황실.lnk";
    private const string RunName = "JjogaeStatus";
    internal string ShortcutPath => Path.Combine(folder, ShortcutName);
    internal static WindowsStartup Current() => new(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.ProcessPath!);
    internal bool Registered => ReadShortcut() is { } link && SamePath(link.Target, executable) && link.Arguments == "--startup" && File.Exists(executable);
    internal bool DisabledByWindows => Registered && Blocked("StartupFolder", ShortcutName) || (!Registered && LegacyRegistered && Blocked("Run", RunName));
    internal bool Enabled => Registered && !DisabledByWindows;
    internal bool LegacyRegistered
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(runKey); return string.Equals(key?.GetValue(RunName) as string, AppInstallation.StartupCommand(executable), StringComparison.OrdinalIgnoreCase); }
    }
    internal string Status => DisabledByWindows ? "Windows에서 사용 안 함으로 설정되어 있습니다. Windows 시작프로그램 설정에서 켜 주세요."
        : Enabled ? "등록 완료 · Windows 로그인 시 백그라운드로 실행합니다."
        : LegacyRegistered ? "기존 시작프로그램 등록을 확인했습니다. 다시 등록하면 Windows 시작프로그램 폴더에 반영합니다."
        : "시작프로그램에 등록되어 있지 않습니다.";
    private bool Blocked(string kind, string name)
    {
        // Read Explorer's choice only; never overwrite Windows' own enable/disable decision.
        using var key = Registry.CurrentUser.OpenSubKey(approvalKey + "\\" + kind);
        return key?.GetValue(name) is byte[] { Length: >= 4 } value && BitConverter.ToUInt32(value, 0) is 3 or 7;
    }
    private static bool SamePath(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
    internal (string Target, string Arguments, string Directory)? ReadShortcut()
    {
        if (!File.Exists(ShortcutPath)) return null;
        var link = (NotificationRegistration.IShellLinkW)new NotificationRegistration.ShellLink();
        try
        {
            ((IPersistFile)link).Load(ShortcutPath, 0);
            var target = new StringBuilder(32768); var arguments = new StringBuilder(32768); var directory = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH: do not resolve or launch a target.
            link.GetArguments(arguments, arguments.Capacity); link.GetWorkingDirectory(directory, directory.Capacity);
            return (target.ToString(), arguments.ToString(), directory.ToString());
        }
        catch (COMException) { return null; }
        finally { Marshal.FinalReleaseComObject(link); }
    }
    internal void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (!File.Exists(executable)) throw new InvalidOperationException("앱 실행 파일을 찾지 못해 시작프로그램에 등록하지 못했습니다.");
            Directory.CreateDirectory(folder);
            var temporary = Path.Combine(folder, ".JjogaeStatus-" + Guid.NewGuid().ToString("N") + ".lnk");
            var link = (NotificationRegistration.IShellLinkW)new NotificationRegistration.ShellLink();
            try
            {
                link.SetPath(executable); link.SetArguments("--startup");
                link.SetWorkingDirectory(Path.GetDirectoryName(executable)!);
                link.SetDescription("Windows 로그인 시 쪼개 상황실 실행"); link.SetIconLocation(executable, 0);
                ((IPersistFile)link).Save(temporary, true);
                File.Move(temporary, ShortcutPath, true);
            }
            finally { Marshal.FinalReleaseComObject(link); if (File.Exists(temporary)) File.Delete(temporary); }
            if (!Registered) throw new InvalidOperationException("Windows 시작프로그램 바로가기 등록을 확인하지 못했습니다.");
        }
        else if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        // Retire our previous entry after the shortcut is verified, avoiding duplicate logon launches.
        using (var key = Registry.CurrentUser.OpenSubKey(runKey, true)) key?.DeleteValue(RunName, false);
        if (enabled ? !Registered : Registered || LegacyRegistered)
            throw new InvalidOperationException("Windows 시작프로그램 설정을 확인하지 못했습니다.");
    }
    internal void MigrateLegacy()
    {
        if (LegacyRegistered && !Blocked("Run", RunName)) SetEnabled(true);
    }
}
