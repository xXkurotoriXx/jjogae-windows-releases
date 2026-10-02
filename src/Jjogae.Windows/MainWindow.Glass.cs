using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Shell;
using Microsoft.Win32;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private HwndSource? glassSource;
    private bool nativeGlassActive;
    private bool forceSolidGlassForTest;

    private void InitializeGlass()
    {
        glassSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        glassSource?.AddHook(GlassMessages);
        ApplyTheme();
    }

    private IntPtr GlassMessages(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // DWM composition changes invalidate the extended client frame.
        if (message == 0x031E) Dispatcher.BeginInvoke(new Action(Render));
        return IntPtr.Zero;
    }

    private bool ApplyNativeGlass()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return false;
        var transparency = Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1), CultureInfo.InvariantCulture) != 0;
        var supported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
        var enabled = supported && transparency && !SystemParameters.HighContrast && !(app.IsTest && forceSolidGlassForTest);
        var mode = dark ? 1 : 0;
        var corners = 2;
        var material = enabled ? 3 : 1; // Desktop Acrylic or no system backdrop.
        if (supported)
        {
            DwmSetWindowAttribute(handle, 20, ref mode, sizeof(int));
            DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
            var caption = dark ? 0x362520 : 0xF9F4F3; // COLORREF, matching the header surface.
            var border = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: no accent outline.
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
            enabled &= DwmSetWindowAttribute(handle, 38, ref material, sizeof(int)) >= 0;
        }
        var chrome = WindowChrome.GetWindowChrome(this);
        chrome.GlassFrameThickness = enabled ? new Thickness(-1) : new Thickness(0);
        var margins = new GlassMargins { Left = enabled ? -1 : 0, Right = enabled ? -1 : 0, Top = enabled ? -1 : 0, Bottom = enabled ? -1 : 0 };
        if (enabled && DwmExtendFrameIntoClientArea(handle, ref margins) < 0)
        {
            enabled = false;
            material = 1;
            DwmSetWindowAttribute(handle, 38, ref material, sizeof(int));
            chrome.GlassFrameThickness = new Thickness(0);
        }
        if (glassSource?.CompositionTarget is { } target)
            target.BackgroundColor = enabled ? Colors.Transparent : ((SolidColorBrush)Brush(dark ? "#171C2A" : "#EEF0F8")).Color;
        return enabled;
    }

    private static LinearGradientBrush GlassGradient(string top, string bottom)
    {
        var gradient = new LinearGradientBrush(Brush(top).Color, Brush(bottom).Color, new Point(0, 0), new Point(0.7, 1));
        gradient.Freeze();
        return gradient;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlassMargins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref GlassMargins margins);
}
