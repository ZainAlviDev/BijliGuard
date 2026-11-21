using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BijliGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGapFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CorrectedAt",
                table: "MeterReadings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhotoUploadId",
                table: "MeterReadings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RolloverConfirmed",
                table: "MeterReadings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RolloverPending",
                table: "MeterReadings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ConsentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingPhotos",
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
                    table.PrimaryKey("PK_ReadingPhotos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeterReadings_PhotoUploadId",
                table: "MeterReadings",
                column: "PhotoUploadId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_UserId_AcceptedAt",
                table: "ConsentRecords",
                columns: new[] { "UserId", "AcceptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingPhotos_HouseholdId",
                table: "ReadingPhotos",
                column: "HouseholdId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsentRecords");

            migrationBuilder.DropTable(
                name: "ReadingPhotos");

            migrationBuilder.DropIndex(
                name: "IX_MeterReadings_PhotoUploadId",
                table: "MeterReadings");

            migrationBuilder.DropColumn(
                name: "CorrectedAt",
                table: "MeterReadings");

            migrationBuilder.DropColumn(
                name: "PhotoUploadId",
                table: "MeterReadings");

            migrationBuilder.DropColumn(
                name: "RolloverConfirmed",
                table: "MeterReadings");

            migrationBuilder.DropColumn(
                name: "RolloverPending",
                table: "MeterReadings");
        }
    }
}
