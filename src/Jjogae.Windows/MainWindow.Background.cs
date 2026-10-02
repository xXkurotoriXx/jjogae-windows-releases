using Microsoft.Win32;
using System.Windows.Threading;

namespace Jjogae.Windows;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer backgroundSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Slider? backgroundOpacitySlider;
    private TextBlock? backgroundOpacityText, backgroundStatus;
    private Button? backgroundChooseButton, backgroundRemoveButton;
    private bool backgroundBusy, backgroundClosed;
    private int backgroundRequest;

    private async Task RestoreBackground()
    {
        var request = ++backgroundRequest;
        try
        {
            var image = await backgroundImages.Load();
            if (backgroundClosed || request != backgroundRequest) return;
            backdrop.Source = image; UpdateBackgroundControls();
        }
        catch
        {
            if (!backgroundClosed && request == backgroundRequest)
            {
                backdrop.Source = null; UpdateBackgroundControls(); status.Text = "배경 이미지를 불러오지 못했습니다. 설정에서 다시 선택해 주세요.";
            }
        }
    }

    private async Task ChooseBackground()
    {
        var picker = new OpenFileDialog { Title = "배경 이미지 선택", Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = false, CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        await SetBackgroundImage(picker.FileName);
    }

    private async Task SetBackgroundImage(string path)
    {
        if (backgroundBusy) return;
        ++backgroundRequest; backgroundBusy = true; UpdateBackgroundControls();
        try
        {
            var image = await backgroundImages.Import(path);
            if (backgroundClosed) return;
            backdrop.Source = image; backdrop.Opacity = app.State.Settings.BackgroundOpacity;
            status.Text = "배경 이미지를 설정했습니다.";
        }
        finally { backgroundBusy = false; if (!backgroundClosed) UpdateBackgroundControls(); }
    }

    private void RemoveBackground()
    {
        if (backgroundBusy) return;
        ++backgroundRequest; backgroundImages.Clear(); backdrop.Source = null; UpdateBackgroundControls();
        status.Text = "배경 이미지를 지웠습니다.";
    }

    private void UpdateBackgroundControls()
    {
        if (backgroundOpacitySlider is not null) backgroundOpacitySlider.IsEnabled = !backgroundBusy;
        if (backgroundChooseButton is not null) backgroundChooseButton.IsEnabled = !backgroundBusy;
        if (backgroundRemoveButton is not null) backgroundRemoveButton.IsEnabled = backgroundImages.Exists && !backgroundBusy;
        if (backgroundStatus is not null) backgroundStatus.Text = backgroundBusy ? "불러오는 중" : backdrop.Source is not null ? "사용자 지정" : backgroundImages.Exists ? "다시 선택 필요" : "없음";
    }

    private void UpdateBackgroundOpacity(double percent)
    {
        app.State.Settings.BackgroundOpacity = percent / 100;
        backdrop.Opacity = app.State.Settings.BackgroundOpacity;
        if (backgroundOpacityText is not null) backgroundOpacityText.Text = $"{app.State.Settings.BackgroundOpacity * 100:0}%";
        backgroundSaveTimer.Stop(); backgroundSaveTimer.Start();
    }

    private void FlushBackgroundPreferences()
    {
        if (!backgroundSaveTimer.IsEnabled) return;
        backgroundSaveTimer.Stop();
        try { app.Save(); }
        catch (Exception error) { if (!backgroundClosed) Error(error); }
    }
}
