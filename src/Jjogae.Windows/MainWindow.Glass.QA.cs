using System.Windows.Interop;
using System.Windows.Shell;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private async Task VerifyGlass(Action<bool, string> check, string? screenshotDirectory)
    {
        if (!app.IsTest) throw new InvalidOperationException("Glass checks require an isolated test profile.");
        foreach (var theme in new[] { "light", "dark" })
        {
            app.State.Settings.Theme = theme;
            forceSolidGlassForTest = false;
            Select(0); await Settle();
            var active = nativeGlassActive;
            check(!AllowsTransparency && glassSource is not null, theme + " / glass retains the native resizable taskbar window");
            check(capsule.CornerRadius.TopLeft == 27 && card is LinearGradientBrush { IsFrozen: true }
                && line is LinearGradientBrush { IsFrozen: true }, theme + " / frozen glass surfaces and capsule navigation");
            if (active)
            {
                var material = 0;
                check(DwmGetWindowAttribute(new WindowInteropHelper(this).Handle, 38, out material, sizeof(int)) >= 0 && material == 3,
                    theme + " / DWM Desktop Acrylic is active");
                check(((SolidColorBrush)Background).Color.A == 0 && ((SolidColorBrush)root.Background).Color.A < 100
                    && WindowChrome.GetWindowChrome(this).GlassFrameThickness.Left == -1,
                    theme + " / acrylic remains visible through the extended WPF client area");
            }
            else check(((SolidColorBrush)root.Background).Color.A == 255, theme + " / unavailable system material has an opaque fallback");
            for (var page = 0; page < sections.Length; page++)
            {
                Select(page); await Settle();
                check(nativeGlassActive == active, theme + " / page " + page + " keeps the window material");
            }
            Select(0); await Settle(); Capture(screenshotDirectory, "glass-" + theme);
            if (screenshotDirectory is not null)
            {
                var origin = PointToScreen(new Point());
                var dpi = VisualTreeHelper.GetDpi(this);
                using var screen = new System.Drawing.Bitmap((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY));
                using (var graphics = System.Drawing.Graphics.FromImage(screen))
                    graphics.CopyFromScreen((int)origin.X, (int)origin.Y, 0, 0, screen.Size);
                screen.Save(Path.Combine(screenshotDirectory, "glass-desktop-" + theme + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            forceSolidGlassForTest = true;
            Render(); await Settle();
            check(!nativeGlassActive && ((SolidColorBrush)root.Background).Color.A == 255
                && WindowChrome.GetWindowChrome(this).GlassFrameThickness.Left == 0,
                theme + " / transparency-disabled fallback restores a solid client area");
            Capture(screenshotDirectory, "glass-solid-" + theme);
            forceSolidGlassForTest = false;
            Render(); await Settle();
            check(nativeGlassActive == active, theme + " / system material restores after fallback");
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
