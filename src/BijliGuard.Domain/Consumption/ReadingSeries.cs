namespace BijliGuard.Domain.Consumption;

/// <summary>One accepted meter reading, as the calculations see it.</summary>
public sealed record ReadingPoint(long Value, DateOnly Date, bool RolloverConfirmed);

/// <summary>
/// Units between accepted readings, including confirmed meter rollovers (technical design section 6.1 and 6.3).
/// A confirmed rollover adds one rollover step: 10^5 for the 5-digit meters this release supports.
/// Pure arithmetic, no I/O.
/// </summary>
public static class ReadingSeries
{
    public const long RolloverStep = 100_000;

    public static long UnitsBetween(ReadingPoint from, ReadingPoint to) =>
        to.Value - from.Value + (to.RolloverConfirmed ? RolloverStep : 0);

    /// <summary>
    /// Units per day for each day in the chain. A gap of several days is spread evenly across the gap
    /// (design section 6.2). Days after <paramref name="upTo"/> are left out. Oldest first.
    /// </summary>
    public static IReadOnlyList<(DateOnly Date, decimal Units)> DailyUnits(
        ReadingPoint start, IReadOnlyList<ReadingPoint> points, DateOnly upTo)
    {
        var result = new List<(DateOnly, decimal)>();
        var previous = start;
        foreach (var point in points)
        {
            var days = point.Date.DayNumber - previous.Date.DayNumber;
            if (days <= 0) continue;

            var perDay = Math.Round(UnitsBetween(previous, point) / (decimal)days, 2);
            for (var offset = 1; offset <= days; offset++)
            {
                var day = previous.Date.AddDays(offset);
                if (day > upTo) break;
                result.Add((day, perDay));
            }
            previous = point;
        }
        return result;
    }
}
