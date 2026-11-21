using BijliGuard.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BijliGuard.Infrastructure.Persistence.Configurations;

public sealed class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> builder)
    {
        builder.ToTable("Households");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.ConsumerReferenceProtected).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ConsumerReferenceMasked).HasMaxLength(32).IsRequired();
        // One household per account in this release.
        builder.HasIndex(x => x.OwnerUserId).IsUnique();
    }
}

public sealed class ElectricityMeterConfiguration : IEntityTypeConfiguration<ElectricityMeter>
{
    public void Configure(EntityTypeBuilder<ElectricityMeter> builder)
    {
        builder.ToTable("Meters");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.MeterNumberProtected).HasMaxLength(512);
        builder.Property(x => x.MeterNumberMasked).HasMaxLength(32);
        builder.HasIndex(x => x.HouseholdId);
    }
}

public sealed class BaselineConfiguration : IEntityTypeConfiguration<Baseline>
{
    public void Configure(EntityTypeBuilder<Baseline> builder)
    {
        builder.ToTable("Baselines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Source).HasConversion<string>().HasMaxLength(10);
        builder.HasIndex(x => new { x.HouseholdId, x.ConfirmedAt });
    }
}

public sealed class MeterReadingConfiguration : IEntityTypeConfiguration<MeterReading>
{
    public void Configure(EntityTypeBuilder<MeterReading> builder)
    {
        builder.ToTable("MeterReadings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(x => x.Warning).HasMaxLength(300);
        // A meter has at most one reading per calendar date.
        builder.HasIndex(x => new { x.MeterId, x.ReadingDate }).IsUnique();
        builder.HasIndex(x => x.PhotoUploadId);
    }
}

public sealed class ReadingPhotoConfiguration : IEntityTypeConfiguration<ReadingPhoto>
{
    public void Configure(EntityTypeBuilder<ReadingPhoto> builder)
    {
        builder.ToTable("ReadingPhotos");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StorageKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.HouseholdId);
    }
}

public sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<BijliGuard.Domain.Consent.ConsentRecord>
{
    public void Configure(EntityTypeBuilder<BijliGuard.Domain.Consent.ConsentRecord> builder)
    {
        builder.ToTable("ConsentRecords");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Version).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => new { x.UserId, x.AcceptedAt });
    }
}

public sealed class BillUploadConfiguration : IEntityTypeConfiguration<BillUpload>
{
    public void Configure(EntityTypeBuilder<BillUpload> builder)
    {
        builder.ToTable("BillUploads");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.StorageKey).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.HouseholdId);
    }
}
