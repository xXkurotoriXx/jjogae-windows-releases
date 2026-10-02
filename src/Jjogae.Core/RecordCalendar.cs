using System.Globalization;

namespace Jjogae.Core;

public static class RecordCalendar
{
    public static DateOnly Day(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Channel.Korea).DateTime);
    public static DateOnly Month(DateOnly day) => new(day.Year, day.Month, 1);
    public static DateOnly[] Days(DateOnly month) => Enumerable.Range(0, DateTime.DaysInMonth(month.Year, month.Month)).Select(i => Month(month).AddDays(i)).ToArray();
    public static DateOnly?[] Cells(DateOnly month)
    {
        month = Month(month);
        var cells = new DateOnly?[42];
        foreach (var day in Days(month)) cells[(int)month.DayOfWeek + day.Day - 1] = day;
        return cells;
    }
    public static DateOnly SelectMonth(DateOnly month, DateOnly previous, DateOnly today)
    {
        var selected = new DateOnly(month.Year, month.Month, Math.Min(previous.Day, DateTime.DaysInMonth(month.Year, month.Month)));
        return selected > today ? today : selected;
    }
    public static int ElapsedMonthDays(DateOnly month, DateOnly today) => Month(month) > Month(today) ? 0
        : Month(month) == Month(today) ? today.Day : DateTime.DaysInMonth(month.Year, month.Month);
    public static long Sum(IEnumerable<long> amounts)
    {
        long total = 0;
        foreach (var amount in amounts.Where(x => x >= 0)) total = long.MaxValue - total < amount ? long.MaxValue : total + amount;
        return total;
    }
    public static string Compact(long amount) => amount >= 100_000_000 ? Unit(amount, 100_000_000, "억")
        : amount >= 10_000 ? Unit(amount, 10_000, "만") : amount.ToString("N0", CultureInfo.GetCultureInfo("ko-KR"));
    private static string Unit(long amount, long divisor, string suffix) => (Math.Floor(amount / (double)divisor * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture) + suffix;
}

public sealed class RecordSelection
{
    public DateOnly SelectedDay { get; private set; }
    public DateOnly SelectedMonth => RecordCalendar.Month(SelectedDay);
    private DateOnly lastToday;
    public RecordSelection(DateOnly today) { SelectedDay = lastToday = today; }
    public void Select(DateOnly day) => SelectedDay = day;
    public void Today(DateOnly today) { SelectedDay = lastToday = today; }
    public void Advance(DateOnly today) { if (SelectedDay == lastToday) SelectedDay = today; lastToday = today; }
}

public enum CheeseMedal { None, Bronze, Silver, Gold }
public sealed record CheeseDay(DateOnly Day, long Total, int Count);

public sealed class CheeseCalendarIndex
{
    private readonly Dictionary<DateOnly, Cheese[]> byDay;
    private readonly Dictionary<DateOnly, CheeseDay> summaries;
    public Cheese[] All { get; }
    public long Total { get; }
    public DateOnly? Earliest { get; }
    public CheeseCalendarIndex(IEnumerable<Cheese> records)
    {
        All = records.Where(x => x.Amount >= 0).DistinctBy(x => x.Id).OrderByDescending(x => x.At).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        byDay = All.GroupBy(x => RecordCalendar.Day(x.At)).ToDictionary(g => g.Key, g => g.ToArray());
        summaries = byDay.ToDictionary(x => x.Key, x => new CheeseDay(x.Key, RecordCalendar.Sum(x.Value.Select(r => r.Amount)), x.Value.Length));
        Total = RecordCalendar.Sum(All.Select(x => x.Amount));
        Earliest = byDay.Count == 0 ? null : byDay.Keys.Min();
    }
    public Cheese[] Records(DateOnly day) => byDay.GetValueOrDefault(day) ?? [];
    public CheeseDay Summary(DateOnly day) => summaries.GetValueOrDefault(day) ?? new(day, 0, 0);
    public CheeseDay[] Month(DateOnly month) => RecordCalendar.Days(month).Select(Summary).ToArray();
    public CheeseDay[] Fortnight(DateOnly through) => Enumerable.Range(-13, 14).Select(i => Summary(through.AddDays(i))).ToArray();
    public long LastDays(int days, DateOnly through) => RecordCalendar.Sum(Enumerable.Range(0, Math.Clamp(days, 0, 366)).Select(i => Summary(through.AddDays(-i)).Total));
    public long MonthlyAverage(DateOnly month, DateOnly today)
    {
        var divisor = RecordCalendar.ElapsedMonthDays(month, today);
        return divisor == 0 ? 0 : RecordCalendar.Sum(Month(month).Where(x => x.Day <= today).Select(x => x.Total)) / divisor;
    }
    public static CheeseMedal Medal(long amount, long maximum) => amount <= 0 || maximum <= 0 ? CheeseMedal.None
        : amount >= maximum - maximum / 3 ? CheeseMedal.Gold
        : amount >= maximum / 3 + (maximum % 3 == 0 ? 0 : 1) ? CheeseMedal.Silver : CheeseMedal.Bronze;
}
