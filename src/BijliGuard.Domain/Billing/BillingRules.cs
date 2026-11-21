namespace BijliGuard.Domain.Billing;

public enum ReadingDecision
{
    Accepted,
    Flagged,
    Rejected
}

public sealed record ReadingCheck(ReadingDecision Decision, string? Code = null, string? Message = null);

/// <summary>Rules for a new meter reading (technical design section 6.3). Pure: no I/O.</summary>
public static class ReadingRules
{
    /// <summary>Code used when a large drop is saved as a flagged rollover, waiting for the user to confirm it.</summary>
    public const string RolloverSuspected = "ROLLOVER_SUSPECTED";

    /// <summary>Average daily use above this is saved but flagged for the user to check.</summary>
    public const decimal HighDailyUnitsThreshold = 100m;

    /// <summary>A drop this large looks like the meter rolled over past its last digit, not a typing error.</summary>
    public const long RolloverDropThreshold = 50_000;

    public static ReadingCheck Evaluate(long value, DateOnly date, DateOnly today, long previousValue, DateOnly previousDate)
    {
        if (value < 0)
            return Reject("INVALID_READING", "The meter reading cannot be negative.");

        if (date > today)
            return Reject("FUTURE_DATE", "The reading date cannot be in the future.");

        if (date <= previousDate)
            return Reject("DATE_NOT_AFTER_PREVIOUS", $"Enter a date after {previousDate:dd MMM yyyy}, the date of your last reading.");

        if (value < previousValue)
        {
            // A drop about the size of the meter's maximum is a rollover (for example 99,980 to 00,015). The user confirms it.
            return previousValue - value >= RolloverDropThreshold
                ? new ReadingCheck(ReadingDecision.Flagged, RolloverSuspected,
                    "The meter reading went back to a low number. This looks like a meter rollover. Confirm it if the meter has reset.")
                : Reject("LOWER_THAN_PREVIOUS", $"The reading is lower than your last reading of {previousValue}. Check the number on the meter.");
        }

        var units = value - previousValue;
        var days = date.DayNumber - previousDate.DayNumber;
        var perDay = units / (decimal)days;
        if (perDay > HighDailyUnitsThreshold)
        {
            return new ReadingCheck(
                ReadingDecision.Flagged,
                "HIGH_DAILY_USE",
                $"That is about {perDay:0.0} units a day, which is unusually high. Please check the reading.");
        }

        return new ReadingCheck(ReadingDecision.Accepted);
    }

    private static ReadingCheck Reject(string code, string message) => new(ReadingDecision.Rejected, code, message);
}

/// <summary>Recognises bill and photo files by their first bytes. The file name and the browser's claim are ignored.</summary>
public static class FileSignatures
{
    public const long MaxBillBytes = 10 * 1024 * 1024;

    /// <summary>Meter photos are smaller than bills (technical design section 7.3).</summary>
    public const long MaxPhotoBytes = 5 * 1024 * 1024;

    public static string? Detect(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            return "image/jpeg";
        if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
            return "image/png";
        if (head.Length >= 4 && head[0] == 0x25 && head[1] == 0x50 && head[2] == 0x44 && head[3] == 0x46)
            return "application/pdf";
        return null;
    }

    public static string ExtensionFor(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "application/pdf" => ".pdf",
        _ => ".bin"
    };
}
