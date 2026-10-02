namespace Jjogae.Core;

public static class BroadcastLogic
{
    public static bool ValidDuration(double value) => double.IsFinite(value) && value is > 0 and <= 2_678_400;
    public static bool ValidDate(DateTimeOffset value) => value.Year is >= 2000 and <= 2100;
    public static Broadcast Normalize(Broadcast value)
    {
        var record = value with { };
        if (record.StartedAt is { } start && !ValidDate(start)) record.StartedAt = null;
        if (record.PublishedAt is { } published && !ValidDate(published)) record.PublishedAt = null;
        if (record.EndedAt is { } end && !ValidDate(end)) record.EndedAt = null;
        if (!ValidDuration(record.Seconds)) record.Seconds = 0;
        if (record.VideoId is null && record.Id.StartsWith("vod:", StringComparison.Ordinal)) record.VideoId = record.Id[4..];
        if (record.VodSeconds is { } vod && !ValidDuration(vod)) record.VodSeconds = null;
        // Version 0.1 derived a VOD start from publication. Re-query details before placing those records on a calendar.
        if (record.TimingSource == "vod") { record.StartedAt = null; record.TimingSource = "vodDuration"; record.VodSeconds ??= record.Seconds; }
        if (record.TimingSource == "observedEnd" && record.StartedAt is not null && record.VodSeconds > 0) record.TimingSource = "vodDuration";
        if (record.TimingSource == "vodDuration") { record.Seconds = record.VodSeconds ?? record.Seconds; record.EndedAt = null; }
        record.ManualTiming?.Validate(DateTimeOffset.UtcNow);
        return record;
    }
    public static bool Same(Broadcast left, Broadcast right)
    {
        if (left.Id == right.Id || left.VideoId is { Length: > 0 } id && id == right.VideoId) return true;
        if (left.VideoId is not null && right.VideoId is not null) return false;
        if (left.LiveSessionId is { Length: > 0 } live && live == right.LiveSessionId) return true;
        return left.StartedAt is { } a && right.StartedAt is { } b && Math.Abs((a - b).TotalSeconds) <= 1;
    }
    public static bool NeedsDetail(Broadcast record, DateTimeOffset now, bool force = false) => record.VideoId is not null
        && (record.StartedAt is null || !ValidDuration(record.Seconds) || record.TimingSource == "observedEnd")
        && (force || record.DetailCheckedAt is null || now - record.DetailCheckedAt >= TimeSpan.FromMinutes(15));
    private static int Priority(Broadcast value) => !ValidDuration(value.Seconds) ? 0 : value.TimingSource switch { "liveTiming" => 3, "vodDuration" => 2, "observedEnd" => 1, _ => 0 };
    private static Broadcast Combine(Broadcast old, Broadcast next)
    {
        var preferred = Priority(next) >= Priority(old) ? next : old;
        var other = ReferenceEquals(preferred, next) ? old : next;
        return Normalize(old with
        {
            Title = !string.IsNullOrWhiteSpace(next.Title) && (next.VideoId is not null || old.VideoId is null) ? next.Title : old.Title,
            VideoId = next.VideoId ?? old.VideoId, LiveSessionId = next.LiveSessionId ?? old.LiveSessionId,
            Url = next.VideoId is not null || old.Url.Length == 0 ? next.Url : old.Url,
            ImageUrl = next.ImageUrl.Length > 0 ? next.ImageUrl : old.ImageUrl,
            PublishedAt = next.PublishedAt ?? old.PublishedAt, DetailCheckedAt = next.DetailCheckedAt ?? old.DetailCheckedAt,
            VodSeconds = next.VodSeconds ?? (next.TimingSource == "vodDuration" && ValidDuration(next.Seconds) ? next.Seconds : old.VodSeconds),
            StartedAt = preferred.StartedAt ?? other.StartedAt,
            EndedAt = preferred.TimingSource == "vodDuration" ? null : preferred.EndedAt ?? other.EndedAt,
            Seconds = preferred.Seconds, TimingSource = preferred.TimingSource,
            ManualTiming = next.ManualTiming is { } timing && (old.ManualTiming is null || timing.UpdatedAt > old.ManualTiming.UpdatedAt) ? timing : old.ManualTiming,
            ThumbnailFilename = old.ThumbnailIsCustom ? old.ThumbnailFilename : next.ThumbnailFilename ?? old.ThumbnailFilename,
            ThumbnailIsCustom = old.ThumbnailIsCustom || next.ThumbnailIsCustom
        });
    }
    public static List<Broadcast> Merge(IEnumerable<Broadcast> existing, Broadcast incoming)
    {
        var records = existing.Select(Normalize).ToList(); var item = Normalize(incoming);
        if (string.IsNullOrEmpty(item.Id)) return records;
        var matches = records.Select((x, i) => (Record: x, Index: i)).Where(x => Same(x.Record, item)).ToArray();
        if (matches.Length == 0) records.Add(item);
        else if (item.VideoId is null && matches.Select(x => x.Record.VideoId).OfType<string>().Distinct().Count() > 1)
            foreach (var match in matches) records[match.Index] = Combine(match.Record, item);
        else
        {
            var combined = Combine(matches[0].Record, item);
            foreach (var match in matches.Skip(1)) combined = Combine(combined, match.Record);
            records[matches[0].Index] = combined;
            foreach (var match in matches.Skip(1).Reverse()) records.RemoveAt(match.Index);
        }
        return BroadcastTiming.Propagate(records).OrderByDescending(x => x.Date).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
    }
    public static Dictionary<DateOnly, double> DailySeconds(IEnumerable<Broadcast> records, DateTimeOffset? now = null)
    {
        var intervals = new List<(DateTimeOffset Start, DateTimeOffset End)>(); var cutoff = now ?? DateTimeOffset.UtcNow;
        foreach (var original in records)
        {
            var record = Normalize(original);
            if (record.EffectiveStartedAt is not { } start || !ValidDuration(record.EffectiveSeconds) || record.ManualTiming is null && record.TimingSource == "observedEnd") continue;
            var end = (record.ManualTiming is not null || record.TimingSource == "liveTiming") && record.EffectiveEndedAt is { } exact ? exact : start.AddSeconds(record.EffectiveSeconds);
            if (end > cutoff) end = cutoff;
            if (end > start && (end - start).TotalDays <= 31) intervals.Add((start, end));
        }
        var totals = new Dictionary<DateOnly, double>(); DateTimeOffset? covered = null;
        foreach (var (start, end) in intervals.OrderBy(x => x.Start).ThenByDescending(x => x.End))
        {
            var uncovered = covered > start ? covered.Value : start;
            if (end > uncovered) { var day = RecordCalendar.Day(start); totals[day] = totals.GetValueOrDefault(day) + (end - uncovered).TotalSeconds; }
            if (covered is null || covered < end) covered = end;
        }
        return totals;
    }
}

public sealed record BroadcastSummary(double Total, int BroadcastDays, double BroadcastDayAverage, double CalendarDayAverage,
    double OverallBroadcastDayAverage, double? PreviousMonthDifference, double MonthlyAverage)
{
    public static BroadcastSummary Calculate(IReadOnlyDictionary<DateOnly, double> days, DateOnly month, DateOnly today)
    {
        month = RecordCalendar.Month(month); var current = days.Where(x => RecordCalendar.Month(x.Key) == month && x.Key <= today && x.Value > 0).ToArray();
        var total = current.Sum(x => x.Value); var average = current.Length == 0 ? 0 : total / current.Length;
        var previous = days.Where(x => RecordCalendar.Month(x.Key) == month.AddMonths(-1) && x.Value > 0).Select(x => x.Value).ToArray();
        var calendarDays = RecordCalendar.ElapsedMonthDays(month, today);
        var all = days.Where(x => x.Key <= today && x.Value > 0).ToArray();
        var months = all.Length == 0 ? 0 : (today.Year - all.Min(x => x.Key).Year) * 12 + today.Month - all.Min(x => x.Key).Month + 1;
        return new(total, current.Length, average, calendarDays == 0 ? 0 : total / calendarDays,
            all.Length == 0 ? 0 : all.Sum(x => x.Value) / all.Length,
            current.Length > 0 && previous.Length > 0 ? average - previous.Average() : null,
            months == 0 ? 0 : all.Sum(x => x.Value) / months);
    }
}
