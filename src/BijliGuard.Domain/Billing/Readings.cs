namespace BijliGuard.Domain.Billing;

public enum BaselineSource
{
    Manual = 1,
    Bill = 2
}

public enum ReadingStatus
{
    Accepted = 1,
    Flagged = 2
}

/// <summary>
/// The known reading at the start of a billing cycle. Every calculation in the cycle starts from here.
/// </summary>
public sealed class Baseline
{
    private Baseline()
    {
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid MeterId { get; private set; }
    public long ReadingValue { get; private set; }
    public DateOnly ReadingDate { get; private set; }
    public DateOnly CycleStart { get; private set; }
    public DateOnly CycleEnd { get; private set; }
    public BaselineSource Source { get; private set; }
    public Guid? BillUploadId { get; private set; }
    public DateTimeOffset ConfirmedAt { get; private set; }

    public static Baseline Create(
        Guid householdId, Guid meterId, long readingValue, DateOnly readingDate,
        DateOnly cycleStart, DateOnly cycleEnd, BaselineSource source, Guid? billUploadId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            MeterId = meterId,
            ReadingValue = readingValue,
            ReadingDate = readingDate,
            CycleStart = cycleStart,
            CycleEnd = cycleEnd,
            Source = source,
            BillUploadId = billUploadId,
            ConfirmedAt = now
        };
}

/// <summary>One confirmed meter reading. Unique per meter and date.</summary>
public sealed class MeterReading
{
    private MeterReading()
    {
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid MeterId { get; private set; }
    public long ReadingValue { get; private set; }
    public DateOnly ReadingDate { get; private set; }
    public ReadingStatus Status { get; private set; }
    public string? Warning { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CorrectedAt { get; private set; }

    /// <summary>A large drop waiting for the user to confirm a meter rollover. Not used in calculations until confirmed.</summary>
    public bool RolloverPending { get; private set; }

    /// <summary>The user confirmed the meter rolled over before this reading. The interval adds one rollover step.</summary>
    public bool RolloverConfirmed { get; private set; }

    /// <summary>The meter photo kept as proof for this reading, if one was attached.</summary>
    public Guid? PhotoUploadId { get; private set; }

    public static MeterReading Create(
        Guid householdId, Guid meterId, long value, DateOnly date, ReadingStatus status, string? warning, DateTimeOffset now,
        bool rolloverPending = false, Guid? photoUploadId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            MeterId = meterId,
            ReadingValue = value,
            ReadingDate = date,
            Status = status,
            Warning = warning,
            CreatedAt = now,
            RolloverPending = rolloverPending,
            PhotoUploadId = photoUploadId
        };

    /// <summary>Replaces a same-day reading after the user corrects it. The change is audited by the caller.</summary>
    public void Correct(long value, ReadingStatus status, string? warning, bool rolloverPending, DateTimeOffset now)
    {
        ReadingValue = value;
        Status = status;
        Warning = warning;
        RolloverPending = rolloverPending;
        RolloverConfirmed = false;
        CorrectedAt = now;
    }

    public void ConfirmRollover()
    {
        RolloverPending = false;
        RolloverConfirmed = true;
        Status = ReadingStatus.Accepted;
        Warning = null;
    }
}

/// <summary>A meter photo kept privately as proof. Metadata is removed before it is stored.</summary>
public sealed class ReadingPhoto
{
    private ReadingPhoto()
    {
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; private set; }

    public static ReadingPhoto Create(Guid householdId, Guid uploadedBy, string storageKey, string contentType, long sizeBytes, string sha256, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            UploadedByUserId = uploadedBy,
            StorageKey = storageKey,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            UploadedAt = now
        };
}

/// <summary>The meter attached to a household. The meter number, when known, is stored encrypted.</summary>
public sealed class ElectricityMeter
{
    private ElectricityMeter()
    {
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public string? MeterNumberProtected { get; private set; }
    public string? MeterNumberMasked { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ElectricityMeter Create(Guid householdId, string? protectedNumber, string? maskedNumber, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            MeterNumberProtected = protectedNumber,
            MeterNumberMasked = maskedNumber,
            CreatedAt = now
        };

    public void SetMeterNumber(string protectedNumber, string maskedNumber)
    {
        MeterNumberProtected = protectedNumber;
        MeterNumberMasked = maskedNumber;
    }
}

/// <summary>A bill file stored privately. Only its household can read it back.</summary>
public sealed class BillUpload
{
    private BillUpload()
    {
    }

    public Guid Id { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; private set; }

    public static BillUpload Create(Guid householdId, Guid uploadedBy, string storageKey, string contentType, long sizeBytes, string sha256, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            HouseholdId = householdId,
            UploadedByUserId = uploadedBy,
            StorageKey = storageKey,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            UploadedAt = now
        };
}
