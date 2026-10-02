namespace Jjogae.Core;

public sealed record BroadcastTimingOverride(DateTimeOffset StartedAt, DateTimeOffset EndedAt, DateTimeOffset UpdatedAt)
{
    [System.Text.Json.Serialization.JsonIgnore] public double Seconds => (EndedAt - StartedAt).TotalSeconds;
    public void Validate(DateTimeOffset now)
    {
        if (!BroadcastLogic.ValidDate(StartedAt) || !BroadcastLogic.ValidDate(EndedAt) || !BroadcastLogic.ValidDuration(Seconds))
            throw new InvalidDataException("종료 시각은 시작 시각보다 늦어야 하며 방송 시간은 31일 이하여야 합니다.");
        if (EndedAt > now || UpdatedAt > now.AddMinutes(1)) throw new InvalidDataException("미래의 방송 시각은 저장할 수 없습니다.");
    }
}

// Port of BroadcastTimingLogic / BroadcastManualAdd. Service timestamps stay intact.
public static class BroadcastTiming
{
    public static List<Broadcast> Update(IEnumerable<Broadcast> source, string id, DateTimeOffset start, DateTimeOffset end, DateTimeOffset now)
    {
        var records = source.Select(x => x with { }).ToList();
        var selected = records.Select((x, i) => (x, i)).Where(x => x.x.Id == id).ToArray();
        if (selected.Length != 1) throw new InvalidDataException("수정할 방송 기록을 정확히 찾지 못했습니다.");
        var timing = new BroadcastTimingOverride(WholeSecond(start), WholeSecond(end), WholeSecond(now)); timing.Validate(now);
        var groups = Groups(records);
        for (var i = 0; i < records.Count; i++) if (groups[i] == groups[selected[0].i]) records[i].ManualTiming = timing;
        return records.OrderByDescending(x => x.Date).ToList();
    }
    public static Broadcast Add(string title, DateTimeOffset start, DateTimeOffset end, IEnumerable<Broadcast> existing, DateTimeOffset now)
    {
        title = title.Trim();
        if (title.Length is < 1 or > 300) throw new InvalidDataException("방송 제목을 1–300자로 입력해 주세요.");
        var timing = new BroadcastTimingOverride(WholeSecond(start), WholeSecond(end), WholeSecond(now)); timing.Validate(now);
        foreach (var record in existing)
        {
            if (record.EffectiveStartedAt is not { } otherStart) continue;
            var otherEnd = record.EffectiveSeconds > 0 ? otherStart.AddSeconds(record.EffectiveSeconds) : record.EffectiveEndedAt ?? otherStart.AddSeconds(1);
            if (timing.StartedAt < otherEnd && timing.EndedAt > otherStart)
                throw new InvalidDataException("이 시간대에 저장된 방송이 있습니다. 기존 방송의 시작·종료 시각을 확인해 주세요.");
        }
        return new Broadcast { Id = "manual:" + Guid.NewGuid().ToString("N"), Title = title, StartedAt = timing.StartedAt,
            EndedAt = timing.EndedAt, Seconds = timing.Seconds, TimingSource = "liveTiming", ManualTiming = timing, Url = Channel.Url };
    }
    public static List<Broadcast> Propagate(List<Broadcast> records)
    {
        var groups = Groups(records);
        var overrides = records.Select((r, i) => (r, group: groups[i])).Where(x => x.r.ManualTiming is not null)
            .GroupBy(x => x.group).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.r.ManualTiming!.UpdatedAt).ThenBy(y => y.r.Id, StringComparer.Ordinal).First().r.ManualTiming);
        return records.Select((r, i) => overrides.TryGetValue(groups[i], out var timing) ? r with { ManualTiming = timing } : r).ToList();
    }
    private static DateTimeOffset WholeSecond(DateTimeOffset date) => DateTimeOffset.FromUnixTimeSeconds(date.ToUnixTimeSeconds());
    private static int[] Groups(IReadOnlyList<Broadcast> records)
    {
        var parents = Enumerable.Range(0, records.Count).ToArray();
        int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
        var identifiers = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < records.Count; i++)
        {
            var r = records[i]; var keys = new List<string> { "record:" + r.Id };
            if (!string.IsNullOrEmpty(r.VideoId)) keys.Add("vod:" + r.VideoId);
            if (!string.IsNullOrEmpty(r.LiveSessionId)) keys.Add("session:" + (r.LiveSessionId.StartsWith("id-", StringComparison.Ordinal) ? r.LiveSessionId[3..] : r.LiveSessionId));
            if (r.StartedAt is { } start) keys.Add("start:" + start.UtcTicks);
            foreach (var key in keys) { if (identifiers.TryGetValue(key, out var previous)) parents[Root(i)] = Root(previous); else identifiers[key] = i; }
        }
        return Enumerable.Range(0, records.Count).Select(Root).ToArray();
    }
}
