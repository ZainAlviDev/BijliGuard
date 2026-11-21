using BijliGuard.Domain.Auth;
using BijliGuard.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BijliGuard.Infrastructure.Persistence.Configurations;

public sealed class EmailOtpConfiguration : IEntityTypeConfiguration<EmailOtp>
{
    public void Configure(EntityTypeBuilder<EmailOtp> builder)
    {
        builder.ToTable("EmailOtps");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Purpose).HasConversion<int>();
        builder.HasIndex(x => new { x.UserId, x.Purpose, x.IssuedAt });
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.FamilyId);
        builder.HasIndex(x => x.UserId);
    }
}

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.Details).HasMaxLength(2000);
        builder.HasIndex(x => new { x.ActorUserId, x.OccurredAt });
    }
}
