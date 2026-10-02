using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;

namespace Jjogae.Windows;

internal static class NotificationRegistration
{
    private static string appId = "io.github.xXkurotoriXx.JjogaeStatus";
    private static string shortcutName = "쪼개 상황실";
    private static bool registered;
    public static void SetProcessIdentity(bool diagnostic)
    {
        if (diagnostic)
        {
            appId += ".Diagnostic";
            shortcutName += " 알림 검사";
        }
        Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(appId));
    }
    public static void EnsureShortcut()
    {
        if (registered) return;
        _ = ToastNotificationManagerCompat.CreateToastNotifier();
        using var identity = Registry.CurrentUser.OpenSubKey(@"Software\Classes\AppUserModelId\" + appId);
        if (!Guid.TryParse(identity?.GetValue("CustomActivator") as string, out var activator))
            throw new InvalidOperationException("Windows 알림 발신자 등록을 확인하지 못했습니다.");
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), shortcutName + ".lnk");
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(Environment.ProcessPath!);
            link.SetWorkingDirectory(Path.GetDirectoryName(Environment.ProcessPath)!);
            link.SetDescription("카페·치지직·YouTube 알림 — 쪼개 상황실");
            link.SetIconLocation(Environment.ProcessPath!, 0);
            var store = (IPropertyStore)link;
            var idKey = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            var activatorKey = new PropertyKey(idKey.FormatId, 26);
            var idValue = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(appId) };
            var activatorValue = new PropVariant { Type = 72, Pointer = Marshal.AllocCoTaskMem(16) };
            try
            {
                Marshal.Copy(activator.ToByteArray(), 0, activatorValue.Pointer, 16);
                store.SetValue(ref idKey, ref idValue); store.SetValue(ref activatorKey, ref activatorValue); store.Commit();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                ((IPersistFile)link).Save(path, true);
            }
            finally { Marshal.FreeCoTaskMem(idValue.Pointer); Marshal.FreeCoTaskMem(activatorValue.Pointer); }
        }
        finally { Marshal.FinalReleaseComObject(link); }
        registered = true;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLink { }
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId) { public Guid FormatId = formatId; public uint PropertyId = propertyId; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count); void GetAt(uint index, out PropertyKey key); void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value); void Commit();
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList); void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey); void SetHotkey(short hotkey); void GetShowCmd(out int command); void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved); void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
