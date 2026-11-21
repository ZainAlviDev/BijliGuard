using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BijliGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Baselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadingValue = table.Column<long>(type: "bigint", nullable: false),
                    ReadingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CycleStart = table.Column<DateOnly>(type: "date", nullable: false),
                    CycleEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BillUploadId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Baselines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillUploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Households",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ConsumerReferenceProtected = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ConsumerReferenceMasked = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TargetUnits = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Households", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MeterReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadingValue = table.Column<long>(type: "bigint", nullable: false),
                    ReadingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Warning = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeterReadings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Meters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeterNumberProtected = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    MeterNumberMasked = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Baselines_HouseholdId_ConfirmedAt",
                table: "Baselines",
                columns: new[] { "HouseholdId", "ConfirmedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BillUploads_HouseholdId",
                table: "BillUploads",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_Households_OwnerUserId",
                table: "Households",
                column: "OwnerUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeterReadings_MeterId_ReadingDate",
                table: "MeterReadings",
                columns: new[] { "MeterId", "ReadingDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Meters_HouseholdId",
                table: "Meters",
                column: "HouseholdId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Baselines");

            migrationBuilder.DropTable(
                name: "BillUploads");

            migrationBuilder.DropTable(
                name: "Households");

            migrationBuilder.DropTable(
                name: "MeterReadings");

            migrationBuilder.DropTable(
                name: "Meters");
        }
    }
}
