using System.Text.Json;

namespace Jjogae.Core;

public sealed class StateStore(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string StatePath => Path.Combine(DirectoryPath, "state.json");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly JsonSerializerOptions StorageJson = new(Json) { WriteIndented = false };

    public AppState Load()
    {
        if (!File.Exists(StatePath)) return new AppState();
        var info = new FileInfo(StatePath);
        if (info.Length > 64 * 1024 * 1024) throw new InvalidDataException("저장 데이터가 너무 큽니다. 원본을 보존하고 실행을 중단합니다.");
        using var document = JsonDocument.Parse(File.ReadAllText(StatePath));
        var state = Read(document.RootElement, out var legacyNotifications);
        if (legacyNotifications) Save(state);
        return state;
    }

    private static AppState Read(JsonElement root, out bool legacyNotifications)
    {
        var state = root.Deserialize<AppState>(Json) ?? throw new InvalidDataException("저장 데이터를 읽을 수 없습니다.");
        state.Normalize();
        legacyNotifications = false;
        // Retain only opaque deduplication keys when removing earlier notification logs.
        foreach (var field in new[] { "activities", "delivered", "pending" })
        {
            if (!root.TryGetProperty(field, out var entries)) continue;
            legacyNotifications = true;
            if (entries.ValueKind != JsonValueKind.Array) continue;
            foreach (var entry in entries.EnumerateArray())
            {
                var id = entry.ValueKind == JsonValueKind.String ? entry.GetString()
                    : entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (!string.IsNullOrWhiteSpace(id)) Policies.RememberNotification(state, id);
            }
        }
        return state;
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = StatePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, StorageJson));
        File.Move(temporary, StatePath, true);
    }

    public static void Export(AppState state, string path)
    {
        var copy = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(state, Json), Json)!;
        copy.Pending.Clear();
        File.WriteAllText(path, JsonSerializer.Serialize(copy, Json));
    }

    public static AppState Import(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("64 MB 이하의 Windows판 백업을 선택해 주세요.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("schemaVersion", out _)) throw new InvalidDataException("Windows판에서 내보낸 백업만 가져올 수 있습니다.");
        var state = Read(document.RootElement, out _);
        state.Normalize(); state.Account = null; state.Pending.Clear(); state.CafeBaseline = false;
        return state;
    }
}
