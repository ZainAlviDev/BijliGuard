using BijliGuard.Application.Common;
using BijliGuard.Domain.Billing;

namespace BijliGuard.Application.Billing;

// ---- Commands ----

public sealed record SaveHouseholdCommand(Disco Provider, string ConsumerReference, int TargetUnits, string? MeterNumber);

public sealed record ChangeTargetCommand(int TargetUnits);

/// <summary>
/// The reading at the start of a billing cycle, from the bill or typed in by hand.
/// The current reading is optional, and when present it becomes the first daily reading.
/// </summary>
public sealed record ConfirmBaselineCommand(
    Guid? BillUploadId,
    long PreviousReading,
    DateOnly PreviousDate,
    DateOnly CycleStart,
    DateOnly CycleEnd,
    long? CurrentReading,
    DateOnly? CurrentDate);

public sealed record SubmitReadingCommand(long Value, DateOnly Date, Guid? PhotoUploadId = null);

public sealed record CorrectReadingCommand(long Value);

// ---- Results ----

public sealed record HouseholdDto(Disco Provider, string ConsumerReferenceMasked, int TargetUnits, string? MeterNumberMasked);

public sealed record BillUploadDto(Guid Id, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);

public sealed record BillFileDto(Stream Content, string ContentType);

/// <summary>A meter photo accepted for a reading. Automatic reading suggestions are not available in this release.</summary>
public sealed record ReadingPhotoDto(
    Guid UploadId,
    string ContentType,
    long SizeBytes,
    long? SuggestedValue,
    string SuggestionStatus);

public sealed record BaselineDto(
    long ReadingValue,
    DateOnly ReadingDate,
    DateOnly CycleStart,
    DateOnly CycleEnd,
    string Source,
    Guid? BillUploadId);

public sealed record ReadingDto(
    Guid Id,
    long Value,
    DateOnly Date,
    string Status,
    string? Warning,
    bool RolloverPending,
    bool CanCorrect,
    Guid? PhotoUploadId);

public sealed record CycleDto(
    DateOnly Start,
    DateOnly End,
    int DaysElapsed,
    int DaysRemaining,
    int UnitsUsed,
    int TargetUnits,
    int RemainingUnits,
    decimal PercentUsed,
    decimal? AveragePerDay,
    decimal? AllowedPerDay,
    decimal? ProjectedUnits,
    string Status);

public sealed record AlertDto(string Code, string Severity, string Message);

public sealed record DailyUnitsDto(DateOnly Date, decimal Units);

/// <summary>Stage tells the web app where the user is: setup, first reading, ongoing, or a finished cycle.</summary>
public sealed record DashboardDto(
    string Stage,
    HouseholdDto? Household,
    CycleDto? Cycle,
    ReadingDto? LatestReading,
    long? TodayUnits,
    IReadOnlyList<ReadingDto> RecentReadings,
    IReadOnlyList<AlertDto> Alerts,
    IReadOnlyList<DailyUnitsDto> DailySeries);

// ---- Ports (implemented in Infrastructure) ----

public interface IBillingRepository
{
    Task<Household?> GetHouseholdForOwnerAsync(Guid ownerUserId, CancellationToken ct);

    Task AddHouseholdAsync(Household household, CancellationToken ct);

    Task<ElectricityMeter?> GetMeterAsync(Guid householdId, CancellationToken ct);

    Task AddMeterAsync(ElectricityMeter meter, CancellationToken ct);

    Task<Baseline?> GetLatestBaselineAsync(Guid householdId, CancellationToken ct);

    Task AddBaselineAsync(Baseline baseline, CancellationToken ct);

    /// <summary>The newest reading that is not waiting for rollover confirmation, on or after <paramref name="from"/>.</summary>
    Task<MeterReading?> GetLatestAcceptedReadingAsync(Guid meterId, DateOnly from, CancellationToken ct);

    /// <summary>The newest reading of any status. Used to find a reading that still needs the user.</summary>
    Task<MeterReading?> GetLatestReadingAsync(Guid meterId, CancellationToken ct);

    /// <summary>Readings from <paramref name="from"/> to <paramref name="to"/> inclusive, oldest first.</summary>
    Task<IReadOnlyList<MeterReading>> ListReadingsAsync(Guid meterId, DateOnly from, DateOnly to, CancellationToken ct);

    Task<MeterReading?> GetReadingAsync(Guid householdId, Guid readingId, CancellationToken ct);

    /// <summary>The latest accepted reading strictly before <paramref name="date"/>, on or after <paramref name="from"/>.</summary>
    Task<MeterReading?> GetPreviousAcceptedReadingAsync(Guid meterId, DateOnly from, DateOnly date, CancellationToken ct);

    Task<bool> ReadingExistsOnDateAsync(Guid meterId, DateOnly date, CancellationToken ct);

    Task<bool> PhotoLinkedAsync(Guid photoUploadId, CancellationToken ct);

    Task AddReadingAsync(MeterReading reading, CancellationToken ct);

    Task AddBillUploadAsync(BillUpload upload, CancellationToken ct);

    Task<BillUpload?> GetBillUploadAsync(Guid householdId, Guid billId, CancellationToken ct);

    Task AddReadingPhotoAsync(ReadingPhoto photo, CancellationToken ct);

    Task<ReadingPhoto?> GetReadingPhotoAsync(Guid householdId, Guid photoId, CancellationToken ct);
}

public interface IFileStorage
{
    /// <summary>Stores the bytes under a generated key. Keys are never built from user input.</summary>
    Task SaveAsync(string key, byte[] content, CancellationToken ct);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
}

/// <summary>Encrypts values that must be recoverable by the server but never readable in the database.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
