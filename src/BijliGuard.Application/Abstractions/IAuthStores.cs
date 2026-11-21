using BijliGuard.Domain.Auth;
using BijliGuard.Domain.Common;

namespace BijliGuard.Application.Abstractions;

public interface IEmailOtpStore
{
    Task<EmailOtp?> GetLatestAsync(Guid userId, OtpPurpose purpose, CancellationToken ct);

    /// <summary>Removes earlier codes for the same user and purpose, then stores the new one.</summary>
    Task ReplaceAsync(EmailOtp otp, CancellationToken ct);
}

public interface IRefreshTokenStore
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct);

    Task<IReadOnlyList<RefreshToken>> FindFamilyAsync(Guid familyId, CancellationToken ct);

    Task<IReadOnlyList<RefreshToken>> FindByUserAsync(Guid userId, CancellationToken ct);

    Task AddAsync(RefreshToken token, CancellationToken ct);
}

public interface IAuditLog
{
    Task RecordAsync(AuditEntry entry, CancellationToken ct);

    /// <summary>The user's newest audit entries, newest first.</summary>
    Task<IReadOnlyList<AuditEntry>> ListForUserAsync(Guid userId, int count, CancellationToken ct);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
