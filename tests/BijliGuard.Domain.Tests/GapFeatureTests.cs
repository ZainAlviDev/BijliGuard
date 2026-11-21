using BijliGuard.Domain.Billing;
using BijliGuard.Domain.Consumption;
using FluentAssertions;
using Xunit;

namespace BijliGuard.Domain.Tests;

public class ReadingRolloverTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    [Fact]
    public void A_large_drop_is_flagged_for_rollover_confirmation_not_rejected()
    {
        // Previous 99,980 and new 15: a drop of 99,965, which is rollover-sized.
        var check = ReadingRules.Evaluate(15, Today, Today, 99_980, Today.AddDays(-1));

        check.Decision.Should().Be(ReadingDecision.Flagged);
        check.Code.Should().Be(ReadingRules.RolloverSuspected);
    }

    [Fact]
    public void A_small_drop_is_still_rejected_as_a_probable_misread()
    {
        var check = ReadingRules.Evaluate(12_400, Today, Today, 12_500, Today.AddDays(-1));

        check.Decision.Should().Be(ReadingDecision.Rejected);
        check.Code.Should().Be("LOWER_THAN_PREVIOUS");
    }
}

public class ReadingSeriesTests
{
    private static readonly DateOnly Nov1 = new(2026, 11, 1);

    [Fact]
    public void Units_between_readings_are_the_plain_difference()
    {
        var from = new ReadingPoint(12_500, Nov1, RolloverConfirmed: false);
        var to = new ReadingPoint(12_525, Nov1.AddDays(1), RolloverConfirmed: false);

        ReadingSeries.UnitsBetween(from, to).Should().Be(25);
    }

    [Fact]
    public void A_confirmed_rollover_adds_one_step_to_its_interval()
    {
        // 99,980 to 00,015 is 35 units once the meter's rollover is counted.
        var from = new ReadingPoint(99_980, Nov1, RolloverConfirmed: false);
        var to = new ReadingPoint(15, Nov1.AddDays(1), RolloverConfirmed: true);

        ReadingSeries.UnitsBetween(from, to).Should().Be(35);
    }

    [Fact]
    public void A_gap_of_several_days_spreads_units_evenly()
    {
        var start = new ReadingPoint(12_500, Nov1, false);
        var points = new[] { new ReadingPoint(12_530, Nov1.AddDays(3), false) };

        var daily = ReadingSeries.DailyUnits(start, points, Nov1.AddDays(3));

        daily.Should().HaveCount(3);
        daily.Select(d => d.Units).Should().AllBeEquivalentTo(10m);
        daily.Select(d => d.Date).Should().Equal(Nov1.AddDays(1), Nov1.AddDays(2), Nov1.AddDays(3));
    }

    [Fact]
    public void Days_after_the_window_end_are_left_out()
    {
        var start = new ReadingPoint(12_500, Nov1, false);
        var points = new[] { new ReadingPoint(12_530, Nov1.AddDays(3), false) };

        var daily = ReadingSeries.DailyUnits(start, points, Nov1.AddDays(1));

        daily.Should().HaveCount(1);
        daily[0].Date.Should().Be(Nov1.AddDays(1));
    }
}

public class ImageMetadataTests
{
    private static byte[] Jpeg(params byte[][] segments)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        foreach (var s in segments) bytes.AddRange(s);
        // Start of scan, then a small payload and EOI.
        bytes.AddRange([0xFF, 0xDA, 0x00, 0x02, 0x11, 0x22, 0xFF, 0xD9]);
        return bytes.ToArray();
    }

    private static byte[] Segment(byte marker, params byte[] payload)
    {
        var length = (ushort)(payload.Length + 2);
        return [0xFF, marker, (byte)(length >> 8), (byte)(length & 0xFF), .. payload];
    }

    [Fact]
    public void JPEG_exif_and_comment_segments_are_removed_and_pixels_are_kept()
    {
        var exif = Segment(0xE1, 0x45, 0x78, 0x69, 0x66, 0x00, 0x00, 0x47, 0x50, 0x53);
        var comment = Segment(0xFE, 0x68, 0x69);
        var icc = Segment(0xE2, 0x49, 0x43, 0x43);
        var input = Jpeg(exif, comment, icc);

        var output = ImageMetadata.StripJpeg(input);

        output.Length.Should().BeLessThan(input.Length);
        // Exif (APP1) and comment (COM) segments are gone.
        Contains(output, new byte[] { 0xFF, 0xE1, 0x00 }).Should().BeFalse();
        Contains(output, new byte[] { 0xFF, 0xFE, 0x00 }).Should().BeFalse();
        // The colour profile (APP2) and the scan data stay.
        Contains(output, new byte[] { 0xFF, 0xE2, 0x00 }).Should().BeTrue();
        output.Should().EndWith(new byte[] { 0xFF, 0xDA, 0x00, 0x02, 0x11, 0x22, 0xFF, 0xD9 });
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return true;
        return false;
    }

    [Fact]
    public void Input_that_is_not_a_jpeg_is_returned_unchanged()
    {
        var notJpeg = new byte[] { 1, 2, 3, 4, 5 };

        ImageMetadata.StripJpeg(notJpeg).Should().BeEquivalentTo(notJpeg);
    }

    [Fact]
    public void PNG_text_and_exif_chunks_are_removed()
    {
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        static byte[] Chunk(string type, byte[] data)
        {
            var len = BitConverter.GetBytes((uint)data.Length).Reverse().ToArray();
            var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            return [.. len, .. typeBytes, .. data, 0, 0, 0, 0];
        }

        var ihdr = Chunk("IHDR", new byte[13]);
        var text = Chunk("tEXt", System.Text.Encoding.ASCII.GetBytes("Author\0someone"));
        var exif = Chunk("eXIf", new byte[] { 1, 2, 3 });
        var iend = Chunk("IEND", []);
        var input = signature.Concat(ihdr).Concat(text).Concat(exif).Concat(iend).ToArray();

        var output = ImageMetadata.StripPng(input);

        Contains(output, System.Text.Encoding.ASCII.GetBytes("tEXt")).Should().BeFalse();
        Contains(output, System.Text.Encoding.ASCII.GetBytes("eXIf")).Should().BeFalse();
        Contains(output, System.Text.Encoding.ASCII.GetBytes("IHDR")).Should().BeTrue();
        output.Should().EndWith(System.Text.Encoding.ASCII.GetBytes("IEND").Concat(new byte[] { 0, 0, 0, 0 }).ToArray());
    }
}
