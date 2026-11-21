using BijliGuard.Application.Abstractions;
using BijliGuard.Domain.Auth;
using BijliGuard.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace BijliGuard.Infrastructure.Persistence.Repositories;

public sealed class EmailOtpStore : IEmailOtpStore
{
    private readonly AppDbContext _db;

    public EmailOtpStore(AppDbContext db) => _db = db;

    public Task<EmailOtp?> GetLatestAsync(Guid userId, OtpPurpose purpose, CancellationToken ct) =>
        _db.EmailOtps
            .Where(x => x.UserId == userId && x.Purpose == purpose)
            .OrderByDescending(x => x.IssuedAt)
            .FirstOrDefaultAsync(ct);

    public async Task ReplaceAsync(EmailOtp otp, CancellationToken ct)
    {
        var previous = await _db.EmailOtps
            .Where(x => x.UserId == otp.UserId && x.Purpose == otp.Purpose)
            .ToListAsync(ct);

        _db.EmailOtps.RemoveRange(previous);
        _db.EmailOtps.Add(otp);
    }
}

public sealed class RefreshTokenStore : IRefreshTokenStore
{
    private readonly AppDbContext _db;

    public RefreshTokenStore(AppDbContext db) => _db = db;

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct) =>
        _db.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash, ct);

    public async Task<IReadOnlyList<RefreshToken>> FindFamilyAsync(Guid familyId, CancellationToken ct) =>
        await _db.RefreshTokens.Where(x => x.FamilyId == familyId).ToListAsync(ct);

    public async Task<IReadOnlyList<RefreshToken>> FindByUserAsync(Guid userId, CancellationToken ct) =>
        await _db.RefreshTokens.Where(x => x.UserId == userId).ToListAsync(ct);

    public Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        _db.RefreshTokens.Add(token);
        return Task.CompletedTask;
    }
}

public sealed class AuditLogStore : IAuditLog
{
    private readonly AppDbContext _db;

    public AuditLogStore(AppDbContext db) => _db = db;

    public Task RecordAsync(AuditEntry entry, CancellationToken ct)
    {
        _db.AuditEntries.Add(entry);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<AuditEntry>> ListForUserAsync(Guid userId, int count, CancellationToken ct) =>
        await _db.AuditEntries
            .Where(x => x.ActorUserId == userId)
            .OrderByDescending(x => x.OccurredAt)
            .Take(count)
            .ToListAsync(ct);
}

public sealed class ConsentRepository(AppDbContext db) : BijliGuard.Application.Consent.IConsentRepository
{
    public Task<BijliGuard.Domain.Consent.ConsentRecord?> GetLatestAsync(Guid userId, CancellationToken ct) =>
        db.ConsentRecords.Where(x => x.UserId == userId)
            .OrderByDescending(x => x.AcceptedAt)
            .FirstOrDefaultAsync(ct);

    public Task AddAsync(BijliGuard.Domain.Consent.ConsentRecord record, CancellationToken ct)
    {
        db.ConsentRecords.Add(record);
        return Task.CompletedTask;
    }
}

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public UnitOfWork(AppDbContext db) => _db = db;

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
}
