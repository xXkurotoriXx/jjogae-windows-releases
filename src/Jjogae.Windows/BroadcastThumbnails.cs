using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Jjogae.Windows;

internal sealed class BroadcastThumbnails(string directory)
{
    private const int MaximumBytes = 10 * 1024 * 1024;
    private readonly string root = Path.Combine(directory, "thumbnails");
    internal string? PathFor(Broadcast record)
    {
        var name = record.ThumbnailFilename;
        if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || name.Contains(':') || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
        var path = Path.Combine(root, name); return File.Exists(path) ? path : null;
    }
    internal Task<string> Save(string id, byte[] bytes) => Task.Run(() =>
    {
        if (bytes.Length is 0 or > MaximumBytes) throw new InvalidDataException("10 MB 이하의 썸네일 이미지를 선택해 주세요.");
        using var input = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 16384 || frame.PixelHeight > 16384 || (long)frame.PixelWidth * frame.PixelHeight > 40_000_000)
            throw new InvalidDataException("이미지가 너무 큽니다.");
        input.Position = 0;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = Math.Min(1920, frame.PixelWidth); bitmap.StreamSource = input; bitmap.EndInit(); bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(root);
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N") + ".png";
        var path = Path.Combine(root, name);
        using var output = new FileStream(path, FileMode.CreateNew); encoder.Save(output);
        return name;
    });
    internal async Task<string> ImportImage(string id, string path)
    {
        if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("10 MB 이하의 이미지를 선택해 주세요.");
        return await Save(id, await File.ReadAllBytesAsync(path));
    }
    internal BitmapImage? Load(Broadcast record)
    {
        var path = PathFor(record); if (path is null) return null;
        try { var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 480; bitmap.UriSource = new Uri(path); bitmap.EndInit(); bitmap.Freeze(); return bitmap; }
        catch { return null; }
    }
    internal void RemoveUnreferenced(IEnumerable<Broadcast> removed, IEnumerable<Broadcast> remaining)
    {
        var retained = remaining.Select(x => x.ThumbnailFilename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var record in removed)
        {
            if (retained.Contains(record.ThumbnailFilename) || PathFor(record) is not { } path) continue;
            try
            {
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) == 0 && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    internal string Export(IEnumerable<Broadcast> records, string destination)
    {
        var target = Path.Combine(destination, "방송 썸네일-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(target); var entries = new List<ThumbnailEntry>();
        foreach (var record in records)
        {
            if (PathFor(record) is not { } source) continue;
            var filename = Path.GetFileName(source); File.Copy(source, Path.Combine(target, filename));
            entries.Add(new(record.Id, filename, record.ThumbnailIsCustom));
        }
        File.WriteAllText(Path.Combine(target, "thumbnails.json"), JsonSerializer.Serialize(entries, StateStore.Json));
        return target;
    }
    internal async Task<int> ImportBundle(string directory, AppState state)
    {
        var manifest = Path.Combine(directory, "thumbnails.json");
        if (new FileInfo(manifest).Length > MaximumBytes) throw new InvalidDataException("썸네일 목록이 너무 큽니다.");
        var entries = JsonSerializer.Deserialize<List<ThumbnailEntry>>(await File.ReadAllTextAsync(manifest), StateStore.Json) ?? [];
        var changes = new List<(Broadcast Record, string Filename, bool Custom)>();
        foreach (var entry in entries.DistinctBy(x => x.RecordId))
        {
            if (entry.Filename != Path.GetFileName(entry.Filename) || entry.Filename.Contains(':') || !entry.Filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("잘못된 썸네일 경로입니다.");
            if (state.Broadcasts.SingleOrDefault(x => x.Id == entry.RecordId) is not { } record) continue;
            var file = Path.Combine(directory, entry.Filename);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("바로가기 이미지는 가져올 수 없습니다.");
            changes.Add((record, await ImportImage(record.Id, file), entry.Custom));
        }
        foreach (var (record, filename, custom) in changes) { record.ThumbnailFilename = filename; record.ThumbnailIsCustom = custom; }
        return changes.Count;
    }
    private sealed record ThumbnailEntry(string RecordId, string Filename, bool Custom);
}
