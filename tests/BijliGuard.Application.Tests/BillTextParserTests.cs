using BijliGuard.Application.Billing;
using FluentAssertions;
using Xunit;

namespace BijliGuard.Application.Tests;

public class BillTextParserTests
{
    // Text as Tesseract 5.4 read it from a clean synthetic bill image.
    private const string CleanBill = """
        Previous Reading: 12500

        Previous Reading Date: 01-11-2026
        Billing Period: 01-11-2026 to 30-11-2026
        Units Consumed: 185
        Current Reading: 12685
        """;

    [Fact]
    public void A_clean_bill_gives_every_value()
    {
        var values = BillTextParser.Parse(CleanBill);

        values.PreviousReading.Should().Be(12_500);
        values.PreviousDate.Should().Be(new DateOnly(2026, 11, 1));
        values.CycleStart.Should().Be(new DateOnly(2026, 11, 1));
        values.CycleEnd.Should().Be(new DateOnly(2026, 11, 30));
        values.UnitsConsumed.Should().Be(185);
        values.CurrentReading.Should().Be(12_685);
    }

    [Fact]
    public void Dates_are_read_day_first()
    {
        // 02-03-2026 is 2 March, not 3 February.
        var values = BillTextParser.Parse("Previous Reading Date: 02-03-2026");

        values.PreviousDate.Should().Be(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public void A_missing_value_is_left_empty_for_the_user_to_type()
    {
        var values = BillTextParser.Parse("Previous Reading: 12500");

        values.PreviousReading.Should().Be(12_500);
        values.CycleStart.Should().BeNull();
        values.CycleEnd.Should().BeNull();
        values.UnitsConsumed.Should().BeNull();
    }

    [Fact]
    public void The_reading_date_label_is_not_mistaken_for_the_reading()
    {
        // "Previous Reading Date" must not be read as a reading of 01112026.
        var values = BillTextParser.Parse("Previous Reading Date: 01-11-2026");

        values.PreviousReading.Should().BeNull();
    }

    [Fact]
    public void Slash_separated_dates_are_accepted()
    {
        var values = BillTextParser.Parse("Billing Period: 01/11/2026 - 30/11/2026");

        values.CycleStart.Should().Be(new DateOnly(2026, 11, 1));
        values.CycleEnd.Should().Be(new DateOnly(2026, 11, 30));
    }

    [Fact]
    public void Text_with_no_billing_values_proposes_nothing()
    {
        var values = BillTextParser.Parse("Thank you for paying your bill on time.");

        values.FoundCount.Should().Be(0);
    }

    [Fact]
    public void Company_consumer_and_meter_numbers_are_read_from_the_header()
    {
        const string header = """
            LESCO Limited
            Consumer No: 12345678901
            Meter No: MTR-998877
            """;

        var values = BillTextParser.Parse(header);

        values.Provider.Should().Be("Lesco");
        values.ConsumerReference.Should().Be("12345678901");
        values.MeterNumber.Should().Be("MTR-998877");
    }

    [Fact]
    public void A_word_that_only_looks_like_a_label_is_not_read_as_a_reference()
    {
        var values = BillTextParser.Parse("Your refund will arrive in 7 days. Thanks for using the service.");

        values.ConsumerReference.Should().BeNull();
    }

    [Fact]
    public void K_Electric_is_recognised_by_name()
    {
        BillTextParser.Parse("K-Electric monthly statement").Provider.Should().Be("KElectric");
    }

    [Fact]
    public void Labels_run_into_Urdu_text_in_a_PDF_are_still_found()
    {
        // Labels printed straight after Urdu or other labels, as PdfPig extracts them from a MEPCO bill.
        var text = "METER INFOMETER NOذ م S-P 1063012MF ر 1" +
                   "PREVIOUS READINGا گ24153PRESENT READINGب\u000024232" +
                   "CONSUMER IDر 1155658155REFERENCE NOر\u000018157370243603";

        var values = BillTextParser.Parse(text);

        values.MeterNumber.Should().Be("S-P 1063012");
        values.PreviousReading.Should().Be(24_153);
        values.CurrentReading.Should().Be(24_232);
        values.ConsumerReference.Should().Be("1155658155");
    }

    [Fact]
    public void Control_characters_from_the_PDF_are_removed_from_identifiers()
    {
        // The NUL before "S-P" comes from the PDF text layer, as seen on the MEPCO bill.
        var values = BillTextParser.Parse("METER NO\u0000S-P 1063012");

        values.MeterNumber.Should().Be("S-P 1063012");
    }

    [Fact]
    public void A_MEPCO_pdf_bill_reads_its_labels_across_lines()
    {
        // Text layer of a MEPCO bill PDF: each value sits on the line below its label.
        const string mepco = """
            MULTAN ELECTRIC POWER COMPANY
            MEPCO GST NO: 04-07-2716-007-55
            REFERENCE NO
            18157370243603
            CONSUMER ID
            1155658155
            METER NO
            S-P 1063012
            MF
            1
            PREVIOUS READING
            24153
            PRESENT READING
            24232
            UNITS
            79
            READING DATE
            28 SEP 26
            ISSUE DATE
            30 SEP 26
            DUE DATE
            12 OCT 26
            """;

        var values = BillTextParser.Parse(mepco);

        values.Provider.Should().Be("Mepco");
        values.ConsumerReference.Should().Be("1155658155", "CONSUMER ID is the account number, not the reference no");
        values.MeterNumber.Should().Be("S-P 1063012");
        values.PreviousReading.Should().Be(24_153);
        values.CurrentReading.Should().Be(24_232);
        values.UnitsConsumed.Should().Be(79, "units are the difference between the two readings");
        values.CurrentDate.Should().Be(new DateOnly(2026, 9, 28));
        // The bill does not print the previous reading date, so one month before the present reading is suggested.
        values.PreviousDate.Should().Be(new DateOnly(2026, 8, 28));
        values.PreviousDateEstimated.Should().BeTrue();

        // The billing period is proposed from the reading dates: it starts on the previous reading date and ends a month later.
        values.CycleStart.Should().Be(new DateOnly(2026, 8, 28));
        values.CycleEnd.Should().Be(new DateOnly(2026, 9, 28));
        values.CycleEstimated.Should().BeTrue();
    }
}
