using System.Text.Json;

namespace Jjogae.Windows;

internal sealed class AppInstallation(string directory, WindowsStartup? startup = null)
{
    internal WindowsStartup Startup { get; } = startup ?? WindowsStartup.Current();
    internal string? StartupError { get; private set; }
    private string FilePath => Path.Combine(directory, "installation.json");
    public bool StartupChoiceMade { get; set; }
    public bool AutomaticUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheck { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
    public void Load()
    {
        if (!File.Exists(FilePath)) return;
        using var json = JsonDocument.Parse(File.ReadAllText(FilePath));
        var root = json.RootElement;
        StartupChoiceMade = root.TryGetProperty("startupChoiceMade", out var startup) && startup.GetBoolean();
        AutomaticUpdates = !root.TryGetProperty("automaticUpdates", out var automatic) || automatic.GetBoolean();
        if (root.TryGetProperty("lastUpdateCheck", out var last) && last.ValueKind == JsonValueKind.String) LastUpdateCheck = last.GetDateTimeOffset();
        if (root.TryGetProperty("lastAttempt", out var attempt) && attempt.ValueKind == JsonValueKind.String) LastAttempt = attempt.GetDateTimeOffset();
    }
    public void Save()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(new { StartupChoiceMade, AutomaticUpdates, LastUpdateCheck, LastAttempt }, StateStore.Json));
        File.Move(FilePath + ".tmp", FilePath, true);
    }
    internal static string StartupCommand(string executable) => "\"" + executable + "\" --startup";
    internal void ReconcileStartup()
    {
        try { Startup.MigrateLegacy(); StartupError = null; }
        catch (Exception) { StartupError = "시작프로그램 등록을 갱신하지 못했습니다. 설정에서 다시 등록해 주세요."; }
    }
    public void SetStartup(bool enabled)
    {
        Startup.SetEnabled(enabled);
        var previousChoice = StartupChoiceMade;
        try { StartupChoiceMade = true; Save(); StartupError = null; }
        catch { StartupChoiceMade = previousChoice; throw; }
    }
}
