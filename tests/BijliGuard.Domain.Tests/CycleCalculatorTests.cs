using BijliGuard.Domain.Consumption;
using FluentAssertions;
using Xunit;

namespace BijliGuard.Domain.Tests;

public class CycleCalculatorTests
{
    [Fact]
    public void Daily_units_are_the_difference_between_readings()
    {
        var result = CycleCalculator.Calculate(12_500, new DateOnly(2026, 11, 1), 12_525, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 30));

        result.UnitsUsed.Should().Be(25);
        result.DaysElapsed.Should().Be(1);
        result.AveragePerDay.Should().Be(25m);
    }

    [Fact]
    public void Worked_example_from_the_design_document_is_reproduced_exactly()
    {
        // Baseline 12,500 on 1 Nov, cycle ends 30 Nov, reading 12,545 on 3 Nov.
        var result = CycleCalculator.Calculate(12_500, new DateOnly(2026, 11, 1), 12_545, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 30));

        result.UnitsUsed.Should().Be(45);
        result.DaysElapsed.Should().Be(2);
        result.DaysRemaining.Should().Be(27);
        result.RemainingUnits.Should().Be(155);
        result.AveragePerDay.Should().Be(22.5m);
        result.AllowedPerDay.Should().Be(5.74m);
        result.ProjectedUnits.Should().Be(675m);
    }

    [Fact]
    public void Remaining_units_can_go_negative_but_allowed_per_day_is_zero()
    {
        var result = CycleCalculator.Calculate(1_000, new DateOnly(2026, 11, 1), 1_250, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 30));

        result.RemainingUnits.Should().Be(-50);
        result.AllowedPerDay.Should().Be(0m);
        result.Status.Should().Be(ConsumptionStatus.OverTarget);
    }

    [Fact]
    public void Averages_are_unknown_on_the_day_of_the_baseline()
    {
        var result = CycleCalculator.Calculate(500, new DateOnly(2026, 11, 1), 500, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));

        result.AveragePerDay.Should().BeNull();
        result.ProjectedUnits.Should().BeNull();
        result.UnitsUsed.Should().Be(0);
    }

    [Fact]
    public void Status_moves_through_the_thresholds()
    {
        var start = new DateOnly(2026, 11, 1);
        var end = new DateOnly(2026, 11, 30);

        CycleCalculator.Calculate(0, start, 160, new DateOnly(2026, 11, 2), end).Status.Should().Be(ConsumptionStatus.Approaching);  // 80%
        CycleCalculator.Calculate(0, start, 180, new DateOnly(2026, 11, 2), end).Status.Should().Be(ConsumptionStatus.NearTarget);   // 90%
        CycleCalculator.Calculate(0, start, 201, new DateOnly(2026, 11, 2), end).Status.Should().Be(ConsumptionStatus.OverTarget);
    }

    [Fact]
    public void Under_80_percent_but_on_pace_to_pass_the_target_is_a_watch()
    {
        // 60 units in 2 days = 30 per day, projected 30 x 30 = 900, well over 200.
        var result = CycleCalculator.Calculate(0, new DateOnly(2026, 11, 1), 60, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 30));

        result.Status.Should().Be(ConsumptionStatus.Watch);
    }

    [Theory]
    [InlineData(-1)]
    public void A_reading_lower_than_the_previous_one_is_rejected(int currentDelta)
    {
        var act = () => CycleCalculator.Calculate(1_000, new DateOnly(2026, 11, 1), 1_000 + currentDelta, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 30));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("currentReading");
    }

    [Fact]
    public void A_date_after_the_cycle_end_is_rejected()
    {
        var act = () => CycleCalculator.Calculate(0, new DateOnly(2026, 11, 1), 10, new DateOnly(2026, 12, 1), new DateOnly(2026, 11, 30));

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("cycleEnd");
    }
}
