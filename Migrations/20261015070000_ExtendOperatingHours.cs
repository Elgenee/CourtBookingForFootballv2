using CourtBookingSystem.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CourtBookingSystem.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration(MigrationId)]
    public class ExtendOperatingHours : Migration
    {
        public const string MigrationId = "20261015070000_ExtendOperatingHours";

        // Runs once for existing installations; new installations use the same
        // 6 AM to next-day 4 AM defaults in DbSeeder.
        public const string UpdateSql = @"
            UPDATE BusinessHours
            SET OpenTime = '06:00:00', CloseTime = '04:00:00', IsClosed = 0;
            UPDATE AboutContents
            SET OpeningHoursTimes = '6:00 AM – 4:00 AM (next day)';
        ";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UpdateSql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Business settings are retained on rollback because each day's
            // previous custom schedule cannot be reconstructed reliably.
        }
    }
}
