using BijliGuard.Domain.Billing;
using FluentAssertions;
using Xunit;

namespace BijliGuard.Domain.Tests;

public class ReadingRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);
    private static readonly DateOnly PrevDate = new(2026, 11, 10);

    [Fact]
    public void A_normal_increase_is_accepted()
    {
        var check = ReadingRules.Evaluate(12_525, new DateOnly(2026, 11, 11), Today, 12_500, PrevDate);

        check.Decision.Should().Be(ReadingDecision.Accepted);
    }

    [Fact]
    public void A_reading_lower_than_the_previous_one_is_rejected()
    {
        var check = ReadingRules.Evaluate(12_400, new DateOnly(2026, 11, 11), Today, 12_500, PrevDate);

        check.Decision.Should().Be(ReadingDecision.Rejected);
        check.Code.Should().Be("LOWER_THAN_PREVIOUS");
    }

    [Fact]
    public void A_very_large_drop_is_treated_as_a_possible_rollover()
    {
        var check = ReadingRules.Evaluate(100, new DateOnly(2026, 11, 11), Today, 99_900, PrevDate);

        check.Code.Should().Be("ROLLOVER_SUSPECTED");
    }

    [Fact]
    public void A_future_date_is_rejected()
    {
        var check = ReadingRules.Evaluate(12_525, new DateOnly(2026, 11, 16), Today, 12_500, PrevDate);

        check.Code.Should().Be("FUTURE_DATE");
    }

    [Fact]
    public void A_date_on_or_before_the_previous_reading_is_rejected()
    {
        ReadingRules.Evaluate(12_525, PrevDate, Today, 12_500, PrevDate).Code.Should().Be("DATE_NOT_AFTER_PREVIOUS");
        ReadingRules.Evaluate(12_525, PrevDate.AddDays(-1), Today, 12_500, PrevDate).Code.Should().Be("DATE_NOT_AFTER_PREVIOUS");
    }

    [Fact]
    public void Unusually_high_daily_use_is_saved_but_flagged()
    {
        // 300 units over 2 days is 150 a day.
        var check = ReadingRules.Evaluate(12_800, new DateOnly(2026, 11, 12), Today, 12_500, PrevDate);

        check.Decision.Should().Be(ReadingDecision.Flagged);
        check.Code.Should().Be("HIGH_DAILY_USE");
    }

    [Fact]
    public void Usage_spread_over_several_days_is_judged_per_day()
    {
        // 300 units over 10 days is 30 a day, which is fine.
        var check = ReadingRules.Evaluate(12_800, new DateOnly(2026, 11, 15), Today, 12_500, new DateOnly(2026, 11, 5));

        check.Decision.Should().Be(ReadingDecision.Accepted);
    }
}

public class FileSignaturesTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }, "application/pdf")]
    public void Recognises_supported_formats_by_their_first_bytes(byte[] head, string expected)
    {
        FileSignatures.Detect(head).Should().Be(expected);
    }

    [Fact]
    public void A_file_with_a_fake_extension_but_other_content_is_rejected()
    {
        // An executable header, even if the file was renamed to .pdf.
        FileSignatures.Detect(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0, 0, 0, 0 }).Should().BeNull();
    }
}
