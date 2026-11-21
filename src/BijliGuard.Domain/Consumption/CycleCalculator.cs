namespace BijliGuard.Domain.Consumption;

public enum ConsumptionStatus
{
    OnTrack,
    Watch,
    Approaching,
    NearTarget,
    OverTarget
}

/// <summary>
/// Result of measuring a billing cycle from a starting reading. Money is never computed here.
/// Averages are null when there is no full day to average over.
/// </summary>
public sealed record CycleSnapshot(
    int UnitsUsed,
    int DaysElapsed,
    int DaysRemaining,
    int RemainingUnits,
    decimal PercentUsed,
    decimal? AveragePerDay,
    decimal? AllowedPerDay,
    decimal? ProjectedUnits,
    ConsumptionStatus Status);

/// <summary>
/// Pure consumption arithmetic (technical design section 6).
/// Daily units = current reading - previous reading. Calendar dates decide the day counts.
/// </summary>
public static class CycleCalculator
{
    public const int DefaultTargetUnits = 200;

    /// <param name="previousReading">Starting meter reading (for example from the bill).</param>
    /// <param name="previousDate">Date of the starting reading.</param>
    /// <param name="currentReading">Latest meter reading. Must not be lower than the starting reading.</param>
    /// <param name="currentDate">Date of the latest reading. Must not be before the starting date.</param>
    /// <param name="cycleEnd">Last day of the billing cycle. Must not be before the current date.</param>
    /// <param name="targetUnits">Consumption target for the cycle.</param>
    public static CycleSnapshot Calculate(
        int previousReading,
        DateOnly previousDate,
        int currentReading,
        DateOnly currentDate,
        DateOnly cycleEnd,
        int targetUnits = DefaultTargetUnits)
    {
        if (currentReading < previousReading)
            throw new ArgumentOutOfRangeException(nameof(currentReading), "Current reading is lower than the previous reading.");
        if (currentDate < previousDate)
            throw new ArgumentOutOfRangeException(nameof(currentDate), "Current date is before the previous reading date.");
        if (cycleEnd < currentDate)
            throw new ArgumentOutOfRangeException(nameof(cycleEnd), "The cycle has already ended.");
        if (targetUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetUnits), "Target must be positive.");

        var unitsUsed = currentReading - previousReading;
        var daysElapsed = currentDate.DayNumber - previousDate.DayNumber;
        var daysRemaining = cycleEnd.DayNumber - currentDate.DayNumber;
        // Cycle length counts both the first and last day, so 1 Nov to 30 Nov is 30 days.
        var cycleDays = daysElapsed + daysRemaining + 1;

        var remaining = targetUnits - unitsUsed;
        var percent = Math.Round(unitsUsed * 100m / targetUnits, 1);

        decimal? average = daysElapsed > 0 ? Math.Round((decimal)unitsUsed / daysElapsed, 2) : null;

        decimal? allowed = daysRemaining > 0
            ? Math.Round(Math.Max(remaining, 0) / (decimal)daysRemaining, 2)
            : null;

        decimal? projected = average is null ? null : Math.Round(average.Value * cycleDays, 0);

        return new CycleSnapshot(
            UnitsUsed: unitsUsed,
            DaysElapsed: daysElapsed,
            DaysRemaining: daysRemaining,
            RemainingUnits: remaining,
            PercentUsed: percent,
            AveragePerDay: average,
            AllowedPerDay: allowed,
            ProjectedUnits: projected,
            Status: StatusFor(percent, projected, targetUnits));
    }

    private static ConsumptionStatus StatusFor(decimal percent, decimal? projected, int targetUnits)
    {
        if (percent > 100m) return ConsumptionStatus.OverTarget;
        if (percent >= 90m) return ConsumptionStatus.NearTarget;
        if (percent >= 80m) return ConsumptionStatus.Approaching;
        // Under 80% today, but on pace to pass the target by the end of the cycle.
        if (projected is not null && projected > targetUnits) return ConsumptionStatus.Watch;
        return ConsumptionStatus.OnTrack;
    }
}
