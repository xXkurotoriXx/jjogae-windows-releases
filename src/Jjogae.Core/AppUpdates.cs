using System.Security.Cryptography;
using System.Text.Json;

namespace Jjogae.Core;

public sealed record AppRelease(Version Version, long AssetId, long ChecksumsId, long Size, string Sha256)
{
    public string DownloadUrl => AppUpdates.DownloadUrl(Version, "JjogaeStatus.exe");
    public string ChecksumsUrl => AppUpdates.DownloadUrl(Version, "SHA256SUMS.txt");
}

public static class AppUpdates
{
    public const string Repository = "xXkurotoriXx/jjogae-windows-releases";
    public static string DownloadUrl(Version version, string name) => $"https://github.com/{Repository}/releases/download/v{version.ToString(3)}/{name}";
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(7);
    public static bool Due(DateTimeOffset? lastCheck, DateTimeOffset now) => lastCheck is null || lastCheck > now || now - lastCheck >= CheckInterval;
    public static Version? VersionTag(string? tag) => tag is not null && tag.StartsWith('v')
        && tag[1..].Split('.').Length == 3 && Version.TryParse(tag[1..], out var version) ? version : null;
    public static AppRelease? Select(JsonElement releases, Version current)
    {
        var candidates = new List<AppRelease>();
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()
                || release.GetProperty("published_at").ValueKind != JsonValueKind.String) continue;
            var version = VersionTag(release.GetProperty("tag_name").GetString());
            if (version is null || version <= current) continue;
            var assets = release.GetProperty("assets").EnumerateArray().Where(x => x.GetProperty("state").GetString() == "uploaded").ToArray();
            var executable = assets.Where(x => x.GetProperty("name").GetString() == "JjogaeStatus.exe").ToArray();
            var checksums = assets.Where(x => x.GetProperty("name").GetString() == "SHA256SUMS.txt").ToArray();
            if (executable.Length != 1 || checksums.Length != 1) continue;
            var digest = executable[0].GetProperty("digest").GetString();
            var size = executable[0].GetProperty("size").GetInt64();
            if (size is <= 0 or > 256 * 1024 * 1024 || digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal)
                || !ValidHash(digest[7..])) continue;
            var assetId = executable[0].GetProperty("id").GetInt64();
            var sumsId = checksums[0].GetProperty("id").GetInt64();
            if (assetId > 0 && sumsId > 0) candidates.Add(new(version, assetId, sumsId, size, digest[7..].ToLowerInvariant()));
        }
        return candidates.OrderByDescending(x => x.Version).FirstOrDefault();
    }
    public static bool ValidHash(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    public static bool MatchesChecksum(string sums, string expected) => sums.TrimStart('\uFEFF').Split('\n')
        .Select(line => line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        .Any(parts => parts.Length == 2 && parts[1] == "JjogaeStatus.exe" && parts[0].Equals(expected, StringComparison.OrdinalIgnoreCase));
    public static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    // File.Replace is atomic on the target volume. Keep the original until the new app confirms startup.
    public static void Replace(string staged, string target, string backup, string expected)
    {
        if (!ValidHash(expected) || Hash(staged) != expected) throw new InvalidDataException("업데이트 체크섬이 일치하지 않습니다.");
        File.Replace(staged, target, backup);
    }
    public static void Rollback(string backup, string target, string failed) => File.Replace(backup, target, failed);
}
