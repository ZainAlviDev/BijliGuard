using System.Security.Cryptography;
using BijliGuard.Application.Abstractions;
using BijliGuard.Application.Common;
using BijliGuard.Domain.Billing;
using BijliGuard.Domain.Common;
using BijliGuard.Domain.Consumption;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace BijliGuard.Application.Billing;

public interface IBillingService
{
    Task<Result<HouseholdDto?>> GetHouseholdAsync(Guid userId, CancellationToken ct);

    Task<Result<HouseholdDto>> SaveHouseholdAsync(Guid userId, SaveHouseholdCommand command, CancellationToken ct);

    Task<Result<HouseholdDto>> ChangeTargetAsync(Guid userId, ChangeTargetCommand command, CancellationToken ct);

    Task<Result<BillUploadDto>> UploadBillAsync(Guid userId, Stream content, CancellationToken ct);

    Task<Result<BillFileDto>> GetBillFileAsync(Guid userId, Guid billId, CancellationToken ct);

    /// <summary>Reads the billing values from an uploaded bill image. Nothing is saved; the user confirms first.</summary>
    Task<Result<ProposedBillValues>> ExtractBillAsync(Guid userId, Guid billId, CancellationToken ct);

    /// <summary>Reads a bill sent for preview. Nothing is stored, and no household is needed yet.</summary>
    Task<Result<ProposedBillValues>> PreviewBillAsync(Guid userId, Stream content, CancellationToken ct);

    Task<Result<BaselineDto>> ConfirmBaselineAsync(Guid userId, ConfirmBaselineCommand command, CancellationToken ct);

    Task<Result<ReadingPhotoDto>> UploadReadingPhotoAsync(Guid userId, Stream content, CancellationToken ct);

    /// <summary>Reads the meter digits from a stored photo. The value is a suggestion; the user confirms it.</summary>
    Task<Result<MeterPhotoReading>> ExtractMeterPhotoAsync(Guid userId, Guid photoId, CancellationToken ct);

    Task<Result<ReadingDto>> SubmitReadingAsync(Guid userId, SubmitReadingCommand command, CancellationToken ct);

    Task<Result<ReadingDto>> ConfirmRolloverAsync(Guid userId, Guid readingId, CancellationToken ct);

    Task<Result<ReadingDto>> CorrectReadingAsync(Guid userId, Guid readingId, CorrectReadingCommand command, CancellationToken ct);

    Task<Result<IReadOnlyList<ReadingDto>>> ListReadingsAsync(Guid userId, CancellationToken ct);

    Task<Result<DashboardDto>> GetDashboardAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// Household, bill and reading use cases. Every operation starts from the caller's own household,
/// so one household can never reach another household's data.
/// </summary>
public sealed class BillingService(
    IBillingRepository repo,
    IFileStorage files,
    ISecretProtector protector,
    IUnitOfWork unitOfWork,
    IAuditLog audit,
    IClock clock,
    ICorrelationIdAccessor correlation,
    IValidator<SaveHouseholdCommand> householdValidator,
    IValidator<ConfirmBaselineCommand> baselineValidator,
    IValidator<SubmitReadingCommand> readingValidator,
    IOcrEngine ocr,
    IPdfTextReader pdfText,
    ILogger<BillingService> logger) : IBillingService
{
    private const string Stage_NeedsHousehold = "needs_household";
    private const string Stage_NeedsBaseline = "needs_baseline";
    private const string Stage_Ready = "ready";
    private const string Stage_CycleEnded = "cycle_ended";

    // Pakistan Standard Time (UTC+5, no daylight saving). Reading dates are calendar dates in PKT.
    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.ToOffset(TimeSpan.FromHours(5)).DateTime);

    public async Task<Result<HouseholdDto?>> GetHouseholdAsync(Guid userId, CancellationToken ct)
    {
        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        if (household is null) return Result<HouseholdDto?>.Success(null);

        var meter = await repo.GetMeterAsync(household.Id, ct);
        return Result<HouseholdDto?>.Success(ToDto(household, meter));
    }

    public async Task<Result<HouseholdDto>> SaveHouseholdAsync(Guid userId, SaveHouseholdCommand command, CancellationToken ct)
    {
        var invalid = Validate(householdValidator, command);
        if (invalid is not null) return Result<HouseholdDto>.Failure(invalid.Error!.Code, invalid.Error.Message, invalid.Error.Fields);

        var now = clock.UtcNow;
        var reference = command.ConsumerReference.Trim();
        var meterNumber = string.IsNullOrWhiteSpace(command.MeterNumber) ? null : command.MeterNumber.Trim();

        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        if (household is null)
        {
            if (reference.Length == 0)
                return Result<HouseholdDto>.Failure(ErrorCodes.ValidationFailed, "Some fields are invalid.",
                    [new FieldError("ConsumerReference", "Enter your consumer reference number.")]);

            household = Household.Create(userId, command.Provider,
                protector.Protect(reference), Household.Mask(reference), command.TargetUnits, now);
            await repo.AddHouseholdAsync(household, ct);
        }
        else
        {
            // A blank reference keeps the saved one; only the company and target change.
            var protectedRef = reference.Length == 0 ? household.ConsumerReferenceProtected : protector.Protect(reference);
            var maskedRef = reference.Length == 0 ? household.ConsumerReferenceMasked : Household.Mask(reference);
            household.Update(command.Provider, protectedRef, maskedRef, command.TargetUnits, now);
        }

        var meter = await repo.GetMeterAsync(household.Id, ct);
        if (meter is null)
        {
            meter = ElectricityMeter.Create(household.Id, meterNumber is null ? null : protector.Protect(meterNumber),
                meterNumber is null ? null : Household.Mask(meterNumber), now);
            await repo.AddMeterAsync(meter, ct);
        }
        else if (meterNumber is not null)
        {
            meter.SetMeterNumber(protector.Protect(meterNumber), Household.Mask(meterNumber));
        }

        await unitOfWork.SaveChangesAsync(ct);
        await RecordAuditAsync(userId, "Billing.HouseholdSaved", ct);
        logger.LogInformation("Billing.HouseholdSaved {HouseholdIdHash} {Provider} {TargetUnits}",
            LogSafe.Hash(household.Id.ToString()), household.Provider.ToString(), household.TargetUnits);

        return Result<HouseholdDto>.Success(ToDto(household, meter));
    }

    public async Task<Result<HouseholdDto>> ChangeTargetAsync(Guid userId, ChangeTargetCommand command, CancellationToken ct)
    {
        if (command.TargetUnits < Household.MinTargetUnits || command.TargetUnits > Household.MaxTargetUnits)
            return Result<HouseholdDto>.Failure(ErrorCodes.ValidationFailed, "Some fields are invalid.",
                [new FieldError("TargetUnits", $"The target must be between {Household.MinTargetUnits} and {Household.MaxTargetUnits} units.")]);

        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        if (household is null)
            return Result<HouseholdDto>.Failure(ErrorCodes.HouseholdRequired, "Set up your household first.");

        household.ChangeTarget(command.TargetUnits, clock.UtcNow);
        var meter = await repo.GetMeterAsync(household.Id, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await RecordAuditAsync(userId, "Billing.TargetChanged", ct);
        logger.LogInformation("Billing.TargetChanged {HouseholdIdHash} {TargetUnits}",
            LogSafe.Hash(household.Id.ToString()), household.TargetUnits);

        return Result<HouseholdDto>.Success(ToDto(household, meter));
    }

    public async Task<Result<BillUploadDto>> UploadBillAsync(Guid userId, Stream content, CancellationToken ct)
    {
        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        if (household is null)
            return Result<BillUploadDto>.Failure(ErrorCodes.HouseholdRequired, "Set up your household before uploading a bill.");

        // Read at most one byte past the limit, so an oversized file is detected without loading all of it.
        var bytes = await ReadLimitedAsync(content, FileSignatures.MaxBillBytes + 1, ct);
        if (bytes.Length == 0)
            return Result<BillUploadDto>.Failure(ErrorCodes.EmptyFile, "The file is empty.");
        if (bytes.Length > FileSignatures.MaxBillBytes)
        {
            logger.LogWarning("Billing.UploadRejected {HouseholdIdHash} Reason {Reason}", LogSafe.Hash(household.Id.ToString()), "too_large");
            return Result<BillUploadDto>.Failure(ErrorCodes.FileTooLarge, "The file is larger than 10 MB. Upload a smaller photo or PDF.");
        }

        var contentType = FileSignatures.Detect(bytes);
        if (contentType is null)
        {
            logger.LogWarning("Billing.UploadRejected {HouseholdIdHash} Reason {Reason}", LogSafe.Hash(household.Id.ToString()), "unsupported_type");
            return Result<BillUploadDto>.Failure(ErrorCodes.UnsupportedFile, "Upload a JPG, PNG or PDF of your bill.");
        }

        var now = clock.UtcNow;
        var key = Guid.NewGuid().ToString("N") + FileSignatures.ExtensionFor(contentType);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));

        await files.SaveAsync(key, bytes, ct);
        var upload = BillUpload.Create(household.Id, userId, key, contentType, bytes.Length, sha256, now);
        await repo.AddBillUploadAsync(upload, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await RecordAuditAsync(userId, "Billing.BillUploaded", ct);

        logger.LogInformation("Billing.BillUploaded {HouseholdIdHash} {ContentType} {SizeBytes}",
            LogSafe.Hash(household.Id.ToString()), contentType, bytes.Length);

        return Result<BillUploadDto>.Success(new BillUploadDto(upload.Id, contentType, bytes.Length, now));
    }

    public async Task<Result<BillFileDto>> GetBillFileAsync(Guid userId, Guid billId, CancellationToken ct)
    {
        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        var upload = household is null ? null : await repo.GetBillUploadAsync(household.Id, billId, ct);
        if (upload is null)
            return Result<BillFileDto>.Failure(ErrorCodes.NotFound, "Bill not found.");

        var stream = await files.OpenReadAsync(upload.StorageKey, ct);
        return Result<BillFileDto>.Success(new BillFileDto(stream, upload.ContentType));
    }

    public async Task<Result<BaselineDto>> ConfirmBaselineAsync(Guid userId, ConfirmBaselineCommand command, CancellationToken ct)
    {
        var invalid = Validate(baselineValidator, command);
        if (invalid is not null) return Result<BaselineDto>.Failure(invalid.Error!.Code, invalid.Error.Message, invalid.Error.Fields);

        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        if (household is null)
            return Result<BaselineDto>.Failure(ErrorCodes.HouseholdRequired, "Set up your household first.");

        var today = Today();
        if (command.CurrentDate is { } currentDate && currentDate > today)
            return Result<BaselineDto>.Failure("FUTURE_DATE", "The current reading date cannot be in the future.");

        if (command.BillUploadId is { } billId)
        {
            var bill = await repo.GetBillUploadAsync(household.Id, billId, ct);
            if (bill is null) return Result<BaselineDto>.Failure(ErrorCodes.NotFound, "Bill not found.");
        }

        var meter = await GetOrCreateMeterAsync(household.Id, ct);
        var now = clock.UtcNow;

        // Check the optional current reading before saving anything, so a bad reading leaves no half-saved state.
        if (command.CurrentReading is { } current && command.CurrentDate is { } date)
        {
            if (await repo.ReadingExistsOnDateAsync(meter.Id, date, ct))
                return Result<BaselineDto>.Failure(ErrorCodes.DuplicateReading, "There is already a reading for that date.");

            var check = ReadingRules.Evaluate(current, date, today, command.PreviousReading, command.PreviousDate);
            if (check.Decision == ReadingDecision.Rejected)
                return Result<BaselineDto>.Failure(check.Code!, check.Message!);
        }

        var baseline = Baseline.Create(
            household.Id, meter.Id, command.PreviousReading, command.PreviousDate,
            command.CycleStart, command.CycleEnd,
            command.BillUploadId is null ? BaselineSource.Manual : BaselineSource.Bill,
            command.BillUploadId, now);
        await repo.AddBaselineAsync(baseline, ct);

        if (command.CurrentReading is { } cur && command.CurrentDate is { } cd)
        {
            var check = ReadingRules.Evaluate(cur, cd, today, command.PreviousReading, command.PreviousDate);
            var reading = MeterReading.Create(household.Id, meter.Id, cur, cd,
                check.Decision == ReadingDecision.Flagged ? ReadingStatus.Flagged : ReadingStatus.Accepted, check.Message, now,
                rolloverPending: check.Code == ReadingRules.RolloverSuspected);
            await repo.AddReadingAsync(reading, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        await RecordAuditAsync(userId, "Billing.BaselineConfirmed", ct);
        logger.LogInformation("Billing.BaselineConfirmed {HouseholdIdHash} {Source} {CycleDays}",
            LogSafe.Hash(household.Id.ToString()), baseline.Source.ToString(),
            command.CycleEnd.DayNumber - command.CycleStart.DayNumber + 1);

        return Result<BaselineDto>.Success(new BaselineDto(
            baseline.ReadingValue, baseline.ReadingDate, baseline.CycleStart, baseline.CycleEnd,
            baseline.Source.ToString(), baseline.BillUploadId));
    }

    /// <summary>
    /// Accepts a meter photo (JPG or PNG, 5 MB at most), removes its metadata and keeps it as proof.
    /// No automatic reading is suggested in this release, so the user always types the number.
    /// </summary>
    public async Task<Result<ReadingPhotoDto>> UploadReadingPhotoAsync(Guid userId, Stream content, CancellationToken ct)
    {
        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        if (household is null)
            return Result<ReadingPhotoDto>.Failure(ErrorCodes.HouseholdRequired, "Set up your household first.");

        var bytes = await ReadLimitedAsync(content, FileSignatures.MaxPhotoBytes + 1, ct);
        if (bytes.Length == 0)
            return Result<ReadingPhotoDto>.Failure(ErrorCodes.EmptyFile, "The file is empty.");
        if (bytes.Length > FileSignatures.MaxPhotoBytes)
            return Result<ReadingPhotoDto>.Failure(ErrorCodes.FileTooLarge, "The photo is larger than 5 MB. Take the photo again at a lower size.");

        var contentType = FileSignatures.Detect(bytes);
        if (contentType is not ("image/jpeg" or "image/png"))
            return Result<ReadingPhotoDto>.Failure(ErrorCodes.UnsupportedFile, "Upload a JPG or PNG photo of your meter.");

        var clean = ImageMetadata.Strip(contentType, bytes);
        var now = clock.UtcNow;
        var key = Guid.NewGuid().ToString("N") + FileSignatures.ExtensionFor(contentType);
        var sha256 = Convert.ToHexString(SHA256.HashData(clean));

        await files.SaveAsync(key, clean, ct);
        var photo = ReadingPhoto.Create(household.Id, userId, key, contentType, clean.Length, sha256, now);
        await repo.AddReadingPhotoAsync(photo, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await RecordAuditAsync(userId, "Reading.PhotoUploaded", ct);

        logger.LogInformation("Reading.PhotoUploaded {HouseholdIdHash} {ContentType} {SizeBytes}",
            LogSafe.Hash(household.Id.ToString()), contentType, clean.Length);

        return Result<ReadingPhotoDto>.Success(new ReadingPhotoDto(photo.Id, contentType, clean.Length, null, "unavailable"));
    }

    public async Task<Result<ReadingDto>> SubmitReadingAsync(Guid userId, SubmitReadingCommand command, CancellationToken ct)
    {
        var invalid = Validate(readingValidator, command);
        if (invalid is not null) return Result<ReadingDto>.Failure(invalid.Error!.Code, invalid.Error.Message, invalid.Error.Fields);

        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        if (household is null) return Result<ReadingDto>.Failure(ErrorCodes.HouseholdRequired, "Set up your household first.");

        var baseline = await repo.GetLatestBaselineAsync(household.Id, ct);
        if (baseline is null) return Result<ReadingDto>.Failure(ErrorCodes.BaselineRequired, "Add your starting reading from a bill first.");

        var today = Today();
        if (command.Date > baseline.CycleEnd || today > baseline.CycleEnd)
            return Result<ReadingDto>.Failure(ErrorCodes.CycleEnded, "This billing cycle has ended. Upload your new bill to continue.");

        var meter = await GetOrCreateMeterAsync(household.Id, ct);

        // A flagged rollover must be confirmed or corrected before the next reading, so the next day's units stay correct.
        var newest = await repo.GetLatestReadingAsync(meter.Id, ct);
        if (newest is { RolloverPending: true })
            return Result<ReadingDto>.Failure(ErrorCodes.RolloverPending,
                "Your last reading looks like a meter rollover. Confirm it or correct it before adding a new reading.");

        if (await repo.ReadingExistsOnDateAsync(meter.Id, command.Date, ct))
            return Result<ReadingDto>.Failure(ErrorCodes.DuplicateReading, "There is already a reading for that date.");

        if (command.PhotoUploadId is { } photoId)
        {
            var photo = await repo.GetReadingPhotoAsync(household.Id, photoId, ct);
            if (photo is null) return Result<ReadingDto>.Failure(ErrorCodes.NotFound, "Photo not found. Upload it again.");
            if (await repo.PhotoLinkedAsync(photoId, ct))
                return Result<ReadingDto>.Failure(ErrorCodes.PhotoAlreadyUsed, "This photo is already used for another reading.");
        }

        // The previous point is the newest accepted reading in this cycle, or the baseline when there is none yet.
        var latest = await repo.GetLatestAcceptedReadingAsync(meter.Id, baseline.ReadingDate, ct);
        var previousValue = latest?.ReadingValue ?? baseline.ReadingValue;
        var previousDate = latest?.ReadingDate ?? baseline.ReadingDate;

        var check = ReadingRules.Evaluate(command.Value, command.Date, today, previousValue, previousDate);
        if (check.Decision == ReadingDecision.Rejected)
        {
            logger.LogInformation("Reading.Rejected {HouseholdIdHash} {Code}", LogSafe.Hash(household.Id.ToString()), check.Code);
            return Result<ReadingDto>.Failure(check.Code!, check.Message!);
        }

        var now = clock.UtcNow;
        var status = check.Decision == ReadingDecision.Flagged ? ReadingStatus.Flagged : ReadingStatus.Accepted;
        var reading = MeterReading.Create(household.Id, meter.Id, command.Value, command.Date, status, check.Message, now,
            rolloverPending: check.Code == ReadingRules.RolloverSuspected, photoUploadId: command.PhotoUploadId);
        await repo.AddReadingAsync(reading, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await RecordAuditAsync(userId, "Reading.Submitted", ct);

        logger.LogInformation("Reading.{Outcome} {HouseholdIdHash} {Code}",
            status == ReadingStatus.Flagged ? "Flagged" : "Accepted", LogSafe.Hash(household.Id.ToString()), check.Code);

        return Result<ReadingDto>.Success(ToReadingDto(reading, today));
    }

    /// <summary>The user confirms a flagged rollover. The interval before this reading then adds one rollover step.</summary>
    public async Task<Result<ReadingDto>> ConfirmRolloverAsync(Guid userId, Guid readingId, CancellationToken ct)
    {
        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        var reading = household is null ? null : await repo.GetReadingAsync(household.Id, readingId, ct);
        if (reading is null) return Result<ReadingDto>.Failure(ErrorCodes.NotFound, "Reading not found.");
        if (!reading.RolloverPending)
            return Result<ReadingDto>.Failure(ErrorCodes.RolloverNotPending, "This reading is not waiting for rollover confirmation.");

        var now = clock.UtcNow;
        reading.ConfirmRollover();
        await unitOfWork.SaveChangesAsync(ct);
        await RecordAuditAsync(userId, "Reading.RolloverConfirmed", ct);
        logger.LogInformation("Reading.RolloverConfirmed {HouseholdIdHash} {CheckedAt}",
            LogSafe.Hash(household!.Id.ToString()), now.ToString("O"));

        return Result<ReadingDto>.Success(ToReadingDto(reading, Today()));
    }

    /// <summary>Replaces today's reading with a corrected value. Only today's reading can be corrected. The change is audited.</summary>
    public async Task<Result<ReadingDto>> CorrectReadingAsync(Guid userId, Guid readingId, CorrectReadingCommand command, CancellationToken ct)
    {
        if (command.Value < 0 || command.Value > 999_999_999)
            return Result<ReadingDto>.Failure(ErrorCodes.ValidationFailed, "Some fields are invalid.",
                [new FieldError("Value", "Enter the number shown on the meter.")]);

        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        var reading = household is null ? null : await repo.GetReadingAsync(household.Id, readingId, ct);
        if (reading is null) return Result<ReadingDto>.Failure(ErrorCodes.NotFound, "Reading not found.");

        var today = Today();
        if (reading.ReadingDate != today)
            return Result<ReadingDto>.Failure(ErrorCodes.ReadingNotCorrectable, "Only today's reading can be corrected.");

        var baseline = await repo.GetLatestBaselineAsync(household!.Id, ct);
        if (baseline is null) return Result<ReadingDto>.Failure(ErrorCodes.BaselineRequired, "Add your starting reading from a bill first.");

        // Compare against the accepted reading before today, or the baseline.
        var previous = await repo.GetPreviousAcceptedReadingAsync(reading.MeterId, baseline.ReadingDate, reading.ReadingDate, ct);
        var previousValue = previous?.ReadingValue ?? baseline.ReadingValue;
        var previousDate = previous?.ReadingDate ?? baseline.ReadingDate;

        var check = ReadingRules.Evaluate(command.Value, reading.ReadingDate, today, previousValue, previousDate);
        if (check.Decision == ReadingDecision.Rejected)
            return Result<ReadingDto>.Failure(check.Code!, check.Message!);

        var now = clock.UtcNow;
        var status = check.Decision == ReadingDecision.Flagged ? ReadingStatus.Flagged : ReadingStatus.Accepted;
        reading.Correct(command.Value, status, check.Message, check.Code == ReadingRules.RolloverSuspected, now);
        await unitOfWork.SaveChangesAsync(ct);
        await RecordAuditAsync(userId, "Reading.Corrected", ct);
        logger.LogInformation("Reading.Corrected {HouseholdIdHash} {Status}", LogSafe.Hash(household.Id.ToString()), status.ToString());

        return Result<ReadingDto>.Success(ToReadingDto(reading, today));
    }

    public async Task<Result<IReadOnlyList<ReadingDto>>> ListReadingsAsync(Guid userId, CancellationToken ct)
    {
        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        if (household is null) return Result<IReadOnlyList<ReadingDto>>.Success([]);
        var baseline = await repo.GetLatestBaselineAsync(household.Id, ct);
        if (baseline is null) return Result<IReadOnlyList<ReadingDto>>.Success([]);

        var meter = await repo.GetMeterAsync(household.Id, ct);
        if (meter is null) return Result<IReadOnlyList<ReadingDto>>.Success([]);

        var today = Today();
        var readings = await repo.ListReadingsAsync(meter.Id, baseline.ReadingDate, baseline.CycleEnd, ct);
        return Result<IReadOnlyList<ReadingDto>>.Success(readings.Select(r => ToReadingDto(r, today)).Reverse().ToList());
    }

    public async Task<Result<DashboardDto>> GetDashboardAsync(Guid userId, CancellationToken ct)
    {
        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        if (household is null)
            return Result<DashboardDto>.Success(new DashboardDto(Stage_NeedsHousehold, null, null, null, null, [], [], []));

        var meter = await repo.GetMeterAsync(household.Id, ct);
        var householdDto = ToDto(household, meter);
        var baseline = meter is null ? null : await repo.GetLatestBaselineAsync(household.Id, ct);
        if (meter is null || baseline is null)
            return Result<DashboardDto>.Success(new DashboardDto(Stage_NeedsBaseline, householdDto, null, null, null, [], [], []));

        var today = Today();
        var cycleEnded = today > baseline.CycleEnd;
        var windowEnd = cycleEnded ? baseline.CycleEnd : today;

        var all = await repo.ListReadingsAsync(meter.Id, baseline.ReadingDate, windowEnd, ct);
        var accepted = all.Where(r => !r.RolloverPending).ToList();

        // Units are chained from the baseline, so a confirmed rollover adds one step to its interval.
        var start = new ReadingPoint(baseline.ReadingValue, baseline.ReadingDate, RolloverConfirmed: false);
        var points = accepted.Select(r => new ReadingPoint(r.ReadingValue, r.ReadingDate, r.RolloverConfirmed)).ToList();
        var previous = start;
        long usedUnits = 0;
        foreach (var point in points)
        {
            usedUnits += ReadingSeries.UnitsBetween(previous, point);
            previous = point;
        }

        var lastAccepted = accepted.LastOrDefault();
        var lastAny = all.LastOrDefault();
        var currentDate = lastAccepted?.ReadingDate ?? baseline.ReadingDate;

        // Units are measured from the baseline. The calculator only needs the difference, so the baseline value is the start.
        var snapshot = CycleCalculator.Calculate(
            checked((int)baseline.ReadingValue), baseline.ReadingDate,
            checked((int)(baseline.ReadingValue + usedUnits)), currentDate,
            baseline.CycleEnd, household.TargetUnits);

        var cycle = new CycleDto(
            baseline.CycleStart, baseline.CycleEnd,
            snapshot.DaysElapsed, snapshot.DaysRemaining, snapshot.UnitsUsed, household.TargetUnits,
            snapshot.RemainingUnits, snapshot.PercentUsed, snapshot.AveragePerDay, snapshot.AllowedPerDay,
            snapshot.ProjectedUnits, snapshot.Status.ToString());

        long? todayUnits = null;
        if (lastAccepted is not null && lastAccepted.ReadingDate == today)
        {
            var before = points.Count > 1 ? points[^2] : start;
            todayUnits = ReadingSeries.UnitsBetween(before, points[^1]);
        }

        var daily = ReadingSeries.DailyUnits(start, points, windowEnd)
            .Select(d => new DailyUnitsDto(d.Date, d.Units))
            .ToList();

        var alerts = BuildAlerts(snapshot, household.TargetUnits, cycleEnded, lastAny, today);
        var recent = all.AsEnumerable().Reverse().Take(10).Select(r => ToReadingDto(r, today)).ToList();

        var stage = cycleEnded ? Stage_CycleEnded : Stage_Ready;
        logger.LogDebug("Dashboard.Calculated {HouseholdIdHash} {Status} {AlertCount}",
            LogSafe.Hash(household.Id.ToString()), snapshot.Status.ToString(), alerts.Count);

        return Result<DashboardDto>.Success(new DashboardDto(
            stage,
            householdDto,
            cycle,
            lastAny is null ? null : ToReadingDto(lastAny, today),
            todayUnits,
            recent,
            alerts,
            daily));
    }

    private List<AlertDto> BuildAlerts(CycleSnapshot s, int target, bool cycleEnded, MeterReading? lastAny, DateOnly today)
    {
        var alerts = new List<AlertDto>();

        if (cycleEnded)
            alerts.Add(new AlertDto("CYCLE_ENDED", "info", "This billing cycle has ended. Upload your new bill to start the next one."));

        switch (s.Status)
        {
            case ConsumptionStatus.OverTarget:
                alerts.Add(new AlertDto("OVER_TARGET", "danger", $"You have passed your {target}-unit target by {-s.RemainingUnits} units."));
                break;
            case ConsumptionStatus.NearTarget:
                alerts.Add(new AlertDto("NEAR_TARGET", "warning", $"Only {s.RemainingUnits} units left. Keep it under {s.AllowedPerDay:0.0} units a day."));
                break;
            case ConsumptionStatus.Approaching:
                alerts.Add(new AlertDto("APPROACHING_TARGET", "warning", $"You have used {s.PercentUsed:0}% of your target. {s.RemainingUnits} units left."));
                break;
            case ConsumptionStatus.Watch:
                alerts.Add(new AlertDto("PROJECTED_OVER_TARGET", "warning",
                    $"At your current average you may pass {target} units. Keep under {s.AllowedPerDay:0.0} units a day."));
                break;
        }

        if (!cycleEnded && (lastAny is null || lastAny.ReadingDate < today))
            alerts.Add(new AlertDto("NO_READING_TODAY", "info", "You have not entered today's meter reading yet."));

        if (lastAny is { RolloverPending: true } pending)
            alerts.Add(new AlertDto("ROLLOVER_PENDING", "warning", pending.Warning ?? "Confirm whether your meter has rolled over."));
        else if (lastAny is { Status: ReadingStatus.Flagged, Warning: not null } flagged)
            alerts.Add(new AlertDto("READING_FLAGGED", "warning", flagged.Warning!));

        return alerts;
    }

    public async Task<Result<MeterPhotoReading>> ExtractMeterPhotoAsync(Guid userId, Guid photoId, CancellationToken ct)
    {
        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        var photo = household is null ? null : await repo.GetReadingPhotoAsync(household.Id, photoId, ct);
        if (photo is null)
            return Result<MeterPhotoReading>.Failure(ErrorCodes.NotFound, "Photo not found. Upload it again.");

        await using var stream = await files.OpenReadAsync(photo.StorageKey, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);

        var text = await ocr.RecognizeAsync(buffer.ToArray(), photo.ContentType, digitsOnly: true, ct);
        if (text is null)
            return Result<MeterPhotoReading>.Failure(ErrorCodes.OcrUnavailable,
                "Automatic reading is not available right now. Type the number you see on the meter.");

        // A meter shows 4 to 9 digits. The longest run is the likeliest reading; the others are offered as alternatives.
        var candidates = System.Text.RegularExpressions.Regex.Matches(text, @"\d{4,9}")
            .Select(m => long.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .OrderByDescending(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture).Length)
            .ToList();

        logger.LogInformation("Reading.PhotoRead {HouseholdIdHash} {CandidateCount}",
            LogSafe.Hash(household!.Id.ToString()), candidates.Count);

        return Result<MeterPhotoReading>.Success(new MeterPhotoReading(candidates.FirstOrDefault(), candidates));
    }

    public async Task<Result<ProposedBillValues>> PreviewBillAsync(Guid userId, Stream content, CancellationToken ct)
    {
        var bytes = await ReadLimitedAsync(content, FileSignatures.MaxBillBytes + 1, ct);
        if (bytes.Length == 0)
            return Result<ProposedBillValues>.Failure(ErrorCodes.EmptyFile, "The file is empty.");
        if (bytes.Length > FileSignatures.MaxBillBytes)
            return Result<ProposedBillValues>.Failure(ErrorCodes.FileTooLarge, "The file is larger than 10 MB. Upload a smaller photo.");

        var contentType = FileSignatures.Detect(bytes);
        if (contentType is null)
            return Result<ProposedBillValues>.Failure(ErrorCodes.UnsupportedFile, "Upload a JPG or PNG of your bill.");
        // A PDF from the company's website carries its text inside the file, so it is read directly (no OCR).
        var text = contentType == "application/pdf"
            ? pdfText.ReadText(bytes)
            : await ocr.RecognizeAsync(bytes, contentType, digitsOnly: false, ct);
        if (text is null && contentType == "application/pdf")
            return Result<ProposedBillValues>.Failure(ErrorCodes.PdfNoText,
                "We could not read text from this PDF. It may be a scanned copy. Upload a photo of the bill instead.");
        if (text is null)
            return Result<ProposedBillValues>.Failure(ErrorCodes.OcrUnavailable,
                "Automatic reading is not available right now. Type the values below.");

        var values = BillTextParser.Parse(text);
        logger.LogInformation("Billing.BillPreviewed {UserIdHash} {FoundCount}", LogSafe.Hash(userId.ToString()), values.FoundCount);
        return Result<ProposedBillValues>.Success(values);
    }

    public async Task<Result<ProposedBillValues>> ExtractBillAsync(Guid userId, Guid billId, CancellationToken ct)
    {
        var household = await repo.GetHouseholdForOwnerAsync(userId, ct);
        var upload = household is null ? null : await repo.GetBillUploadAsync(household.Id, billId, ct);
        if (upload is null)
            return Result<ProposedBillValues>.Failure(ErrorCodes.NotFound, "Bill not found.");

        await using var stream = await files.OpenReadAsync(upload.StorageKey, ct);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();

        // A PDF from the company's website carries its text inside the file, so it is read directly (no OCR).
        var text = upload.ContentType == "application/pdf"
            ? pdfText.ReadText(bytes)
            : await ocr.RecognizeAsync(bytes, upload.ContentType, digitsOnly: false, ct);
        if (text is null && upload.ContentType == "application/pdf")
            return Result<ProposedBillValues>.Failure(ErrorCodes.PdfNoText,
                "We could not read text from this PDF. It may be a scanned copy. Upload a photo of the bill instead.");
        if (text is null)
            return Result<ProposedBillValues>.Failure(ErrorCodes.OcrUnavailable,
                "Automatic reading is not available right now. Type the values below.");

        var values = BillTextParser.Parse(text);
        logger.LogInformation("Billing.BillExtracted {HouseholdIdHash} {FoundCount}",
            LogSafe.Hash(household!.Id.ToString()), values.FoundCount);
        return Result<ProposedBillValues>.Success(values);
    }

    private async Task<ElectricityMeter> GetOrCreateMeterAsync(Guid householdId, CancellationToken ct)
    {
        var meter = await repo.GetMeterAsync(householdId, ct);
        if (meter is not null) return meter;

        meter = ElectricityMeter.Create(householdId, null, null, clock.UtcNow);
        await repo.AddMeterAsync(meter, ct);
        return meter;
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream source, long limit, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > limit) break;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private HouseholdDto ToDto(Household h, ElectricityMeter? meter) =>
        new(h.Provider, h.ConsumerReferenceMasked, h.TargetUnits, meter?.MeterNumberMasked);

    /// <summary>A correction is offered only for today's reading, which is always the newest one that can be corrected.</summary>
    private static ReadingDto ToReadingDto(MeterReading r, DateOnly today) =>
        new(r.Id, r.ReadingValue, r.ReadingDate, r.Status.ToString(), r.Warning, r.RolloverPending,
            CanCorrect: r.ReadingDate == today, r.PhotoUploadId);

    private async Task RecordAuditAsync(Guid userId, string action, CancellationToken ct)
    {
        await audit.RecordAsync(AuditEntry.Create(userId, action, clock.UtcNow, correlation.CorrelationId), ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static Result? Validate<T>(IValidator<T> validator, T command)
    {
        var validation = validator.Validate(command);
        if (validation.IsValid) return null;
        var fields = validation.Errors.Select(e => new FieldError(e.PropertyName, e.ErrorMessage)).ToList();
        return Result.Failure(ErrorCodes.ValidationFailed, "Some fields are invalid.", fields);
    }
}
