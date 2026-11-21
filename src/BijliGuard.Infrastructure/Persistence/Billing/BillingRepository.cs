using BijliGuard.Application.Billing;
using BijliGuard.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace BijliGuard.Infrastructure.Persistence.Billing;

public sealed class BillingRepository(AppDbContext db) : IBillingRepository
{
    public Task<Household?> GetHouseholdForOwnerAsync(Guid ownerUserId, CancellationToken ct) =>
        db.Households.FirstOrDefaultAsync(x => x.OwnerUserId == ownerUserId, ct);

    public Task AddHouseholdAsync(Household household, CancellationToken ct)
    {
        db.Households.Add(household);
        return Task.CompletedTask;
    }

    public Task<ElectricityMeter?> GetMeterAsync(Guid householdId, CancellationToken ct) =>
        db.Meters.Where(x => x.HouseholdId == householdId).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);

    public Task AddMeterAsync(ElectricityMeter meter, CancellationToken ct)
    {
        db.Meters.Add(meter);
        return Task.CompletedTask;
    }

    public Task<Baseline?> GetLatestBaselineAsync(Guid householdId, CancellationToken ct) =>
        db.Baselines.Where(x => x.HouseholdId == householdId)
            .OrderByDescending(x => x.ConfirmedAt)
            .FirstOrDefaultAsync(ct);

    public Task AddBaselineAsync(Baseline baseline, CancellationToken ct)
    {
        db.Baselines.Add(baseline);
        return Task.CompletedTask;
    }

    public Task<MeterReading?> GetLatestReadingAsync(Guid meterId, CancellationToken ct) =>
        db.MeterReadings.Where(x => x.MeterId == meterId)
            .OrderByDescending(x => x.ReadingDate)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<MeterReading>> ListReadingsAsync(Guid meterId, DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.MeterReadings
            .Where(x => x.MeterId == meterId && x.ReadingDate >= from && x.ReadingDate <= to)
            .OrderBy(x => x.ReadingDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<MeterReading>> RecentReadingsAsync(Guid meterId, int count, CancellationToken ct) =>
        await db.MeterReadings
            .Where(x => x.MeterId == meterId)
            .OrderByDescending(x => x.ReadingDate)
            .Take(count)
            .ToListAsync(ct);

    public Task<bool> ReadingExistsOnDateAsync(Guid meterId, DateOnly date, CancellationToken ct) =>
        db.MeterReadings.AnyAsync(x => x.MeterId == meterId && x.ReadingDate == date, ct);

    public Task AddReadingAsync(MeterReading reading, CancellationToken ct)
    {
        db.MeterReadings.Add(reading);
        return Task.CompletedTask;
    }

    public Task AddBillUploadAsync(BillUpload upload, CancellationToken ct)
    {
        db.BillUploads.Add(upload);
        return Task.CompletedTask;
    }

    public Task<BillUpload?> GetBillUploadAsync(Guid householdId, Guid billId, CancellationToken ct) =>
        db.BillUploads.FirstOrDefaultAsync(x => x.Id == billId && x.HouseholdId == householdId, ct);

    public Task<MeterReading?> GetLatestAcceptedReadingAsync(Guid meterId, DateOnly from, CancellationToken ct) =>
        db.MeterReadings
            .Where(x => x.MeterId == meterId && x.ReadingDate >= from && !x.RolloverPending)
            .OrderByDescending(x => x.ReadingDate)
            .FirstOrDefaultAsync(ct);

    public Task<MeterReading?> GetReadingAsync(Guid householdId, Guid readingId, CancellationToken ct) =>
        db.MeterReadings.FirstOrDefaultAsync(x => x.Id == readingId && x.HouseholdId == householdId, ct);

    public Task<MeterReading?> GetPreviousAcceptedReadingAsync(Guid meterId, DateOnly from, DateOnly date, CancellationToken ct) =>
        db.MeterReadings
            .Where(x => x.MeterId == meterId && x.ReadingDate >= from && x.ReadingDate < date && !x.RolloverPending)
            .OrderByDescending(x => x.ReadingDate)
            .FirstOrDefaultAsync(ct);

    public Task<bool> PhotoLinkedAsync(Guid photoUploadId, CancellationToken ct) =>
        db.MeterReadings.AnyAsync(x => x.PhotoUploadId == photoUploadId, ct);

    public Task AddReadingPhotoAsync(ReadingPhoto photo, CancellationToken ct)
    {
        db.ReadingPhotos.Add(photo);
        return Task.CompletedTask;
    }

    public Task<ReadingPhoto?> GetReadingPhotoAsync(Guid householdId, Guid photoId, CancellationToken ct) =>
        db.ReadingPhotos.FirstOrDefaultAsync(x => x.Id == photoId && x.HouseholdId == householdId, ct);
}
