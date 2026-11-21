using System.Globalization;
using System.Text.RegularExpressions;

namespace BijliGuard.Application.Billing;

/// <summary>
/// Values proposed from a bill. Every field is optional: a missing value is left for the user to type.
/// Nothing here is saved until the user confirms it.
/// </summary>
public sealed record ProposedBillValues(
    long? PreviousReading,
    DateOnly? PreviousDate,
    DateOnly? CycleStart,
    DateOnly? CycleEnd,
    long? CurrentReading,
    DateOnly? CurrentDate,
    int? UnitsConsumed,
    string? Provider = null,
    string? ConsumerReference = null,
    string? MeterNumber = null,
    bool PreviousDateEstimated = false,
    bool CycleEstimated = false)
{
    public int FoundCount =>
        new object?[] { PreviousReading, PreviousDate, CycleStart, CycleEnd, CurrentReading, CurrentDate, UnitsConsumed, Provider, ConsumerReference, MeterNumber }
            .Count(v => v is not null);
}

/// <summary>A meter reading proposed from a photo. The user confirms or corrects it before it is saved.</summary>
public sealed record MeterPhotoReading(long? SuggestedValue, IReadOnlyList<long> Candidates);

/// <summary>
/// Finds the billing values in text from a bill (read by OCR from a photo, or from a PDF's text).
/// Labels are matched loosely, because bill layouts differ between companies. Pure: no I/O, so it can be
/// tested with saved text.
/// </summary>
public static class BillTextParser
{
    // Dates: 01-11-2026, 01/11/2026, 2026-11-01, or "28 SEP 26" as printed on MEPCO bills.
    private const string DatePattern = @"(\d{1,2}[-/.]\d{1,2}[-/.]\d{2,4}|\d{4}-\d{2}-\d{2}|\d{1,2}\s+[A-Za-z]{3}\s+\d{2,4})";

    // Values often sit on the line after their label, so the gap may cross lines. A label is still required,
    // so unrelated numbers are not picked up.
    // Labels are checked with "not next to another Latin letter" rather than \b: Urdu letters count as word
    // characters, so \b fails on bills where Urdu text runs straight into a label.
    private static readonly Regex PreviousReading = new(@"(?<![A-Za-z])previous\s+(?:meter\s+)?reading(?![A-Za-z])(?!\s*date)\D{0,80}?(\d{3,9})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CurrentReading = new(@"(?<![A-Za-z])(?:current|present)\s+(?:meter\s+)?reading(?![A-Za-z])(?!\s*date)\D{0,80}?(\d{3,9})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PreviousDate = new(@"previous\s+(?:meter\s+)?reading\s+date\D{0,60}?" + DatePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Reading date" alone is the date of the present reading, but not when it follows "previous".
    private static readonly Regex CurrentDate = new(@"(?:(?:current|present)\s+(?:meter\s+)?reading\s+date|(?<!previous\s)(?<!previous\s\s)(?<![A-Za-z])reading\s+date)\D{0,60}?" + DatePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Period = new(@"(?:billing\s+period|bill\s+period|period)\D{0,60}?" + DatePattern + @"\s*(?:to|-|–|till|until)\s*" + DatePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Units = new(@"units?\s+(?:consumed|used|consumption)\D{0,60}?(\d{1,5})|consumption\D{0,60}?(\d{1,5})\s*units?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Consumer ID" is the number the company uses for the account. It is preferred over "Reference No",
    // which many bills use for payment slips.
    private static readonly Regex ConsumerId = new(@"(?<![A-Za-z])consumer\s*(?:id|no|number)(?![A-Za-z])\.?\D{0,40}?(\d{6,24})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A label is required ("Consumer No", "Reference Number", "Account #"), so ordinary words are not picked up.
    private static readonly Regex ConsumerRef = new(@"(?<![A-Za-z])(?:consumer|reference|ref|account)\s*(?:(?:no|number|num|id)(?![A-Za-z])\.?|#)\s*:?\s*([A-Za-z0-9][A-Za-z0-9-]{5,23})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Meter numbers may carry a short prefix, such as "S-P 1063012". The prefix is kept as printed.
    // No leading check here: on MEPCO bills the label is printed straight after "METER INFO" ("INFOMETER NO").
    private static readonly Regex MeterNo = new(@"meter\s*(?:(?:no|number|num)(?![A-Za-z])\.?|#)\D{0,120}?(\S{1,4}\s?\d{4,12}|[A-Za-z0-9][A-Za-z0-9-]{3,19})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ProposedBillValues Parse(string text)
    {
        var normalized = text.Replace(' ', ' ');

        var period = Period.Match(normalized);
        var cycleStart = period.Success ? ParseDate(period.Groups[1].Value) : null;
        var cycleEnd = period.Success ? ParseDate(period.Groups[2].Value) : null;

        var units = Units.Match(normalized);
        var unitsValue = units.Success
            ? int.Parse(units.Groups[1].Success ? units.Groups[1].Value : units.Groups[2].Value, CultureInfo.InvariantCulture)
            : (int?)null;

        var previous = Long(PreviousReading.Match(normalized));

        // Many bills (MEPCO included) print the present reading date but not the previous one. Meters are usually
        // read about a month apart, so one month before the present reading is offered as an estimate to check.
        var previousDate = ParseDate(PreviousDate.Match(normalized).Groups[1].Value);
        var previousEstimated = false;
        var presentDate = ParseDate(CurrentDate.Match(normalized).Groups[1].Value);
        if (previousDate is null && presentDate is not null)
        {
            previousDate = presentDate.Value.AddMonths(-1);
            previousEstimated = true;
        }

        // The billing period starts on the previous reading date and runs to the same date next month.
        // When the bill does not print the period, these are proposed from the reading date, and marked as estimates.
        var cycleEstimated = false;
        if (cycleStart is null && previousDate is not null)
        {
            cycleStart = previousDate;
            cycleEstimated = true;
        }
        if (cycleEnd is null && cycleStart is not null)
        {
            cycleEnd = cycleStart.Value.AddMonths(1);
            cycleEstimated = true;
        }
        var current = Long(CurrentReading.Match(normalized));

        // Many bills print only the two readings. Units are then the difference between them.
        if (unitsValue is null && previous is not null && current is not null && current >= previous)
            unitsValue = (int)(current - previous);

        var consumer = ConsumerId.Match(normalized);
        if (!consumer.Success) consumer = ConsumerRef.Match(normalized);
        var meter = MeterNo.Match(normalized);

        return new ProposedBillValues(
            Provider: FindProvider(normalized),
            ConsumerReference: consumer.Success ? Clean(consumer.Groups[1].Value) : null,
            MeterNumber: meter.Success ? Clean(meter.Groups[1].Value) : null,
            PreviousReading: previous,
            PreviousDate: previousDate,
            PreviousDateEstimated: previousEstimated,
            CycleEstimated: cycleEstimated,
            CycleStart: cycleStart,
            CycleEnd: cycleEnd,
            CurrentReading: current,
            CurrentDate: ParseDate(CurrentDate.Match(normalized).Groups[1].Value),
            UnitsConsumed: unitsValue);
    }

    /// <summary>The company name as the app stores it (the Disco enum name), if the bill names one.</summary>
    private static string? FindProvider(string text)
    {
        var names = new (string Keyword, string Provider)[]
        {
            ("LESCO", "Lesco"), ("IESCO", "Iesco"), ("MEPCO", "Mepco"), ("MULTAN ELECTRIC", "Mepco"),
            ("FESCO", "Fesco"), ("GEPCO", "Gepco"), ("HESCO", "Hesco"), ("SEPCO", "Sepco"), ("PESCO", "Pesco"),
            ("QESCO", "Qesco"), ("K-ELECTRIC", "KElectric"), ("K ELECTRIC", "KElectric")
        };
        foreach (var (keyword, provider) in names)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return provider;
        }
        return null;
    }

    /// <summary>PDF text can carry control characters (for example NUL) next to a value. They are not part of it.</summary>
    private static string Clean(string value) =>
        Regex.Replace(value, @"\p{C}", string.Empty).Trim();

    private static long? Long(Match match) =>
        match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>Pakistani bills write day first (01-11-2026), and some print the month name (28 SEP 26).</summary>
    internal static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var formats = new[]
        {
            "d-M-yyyy", "d/M/yyyy", "d.M.yyyy", "d-M-yy", "d/M/yy", "d.M.yy", "yyyy-MM-dd",
            "d MMM yy", "d MMM yyyy", "dd MMM yy", "dd MMM yyyy"
        };
        var cleaned = Regex.Replace(raw.Trim(), @"\s+", " ");
        return DateOnly.TryParseExact(cleaned, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
