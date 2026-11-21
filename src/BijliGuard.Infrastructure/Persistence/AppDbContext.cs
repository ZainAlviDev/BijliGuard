using BijliGuard.Domain.Auth;
using BijliGuard.Domain.Billing;
using BijliGuard.Domain.Common;
using BijliGuard.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BijliGuard.Infrastructure.Persistence;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<EmailOtp> EmailOtps => Set<EmailOtp>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<ElectricityMeter> Meters => Set<ElectricityMeter>();
    public DbSet<Baseline> Baselines => Set<Baseline>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<BillUpload> BillUploads => Set<BillUpload>();
    public DbSet<ReadingPhoto> ReadingPhotos => Set<ReadingPhoto>();
    public DbSet<BijliGuard.Domain.Consent.ConsentRecord> ConsentRecords => Set<BijliGuard.Domain.Consent.ConsentRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
