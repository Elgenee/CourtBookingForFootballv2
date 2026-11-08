using Microsoft.EntityFrameworkCore.Migrations;

namespace CourtBookingSystem.Migrations
{
    public partial class AddBookingIsWalkIn : Migration
    {
        // The counter flow historically prepended this marker, including when
        // customers supplied their own email. Backfill once, before notes become editable.
        public const string BackfillSql = @"
            UPDATE Bookings SET IsWalkIn = 1
            WHERE UserId IS NULL
                AND (Notes = 'Walk-in (counter)' OR Notes LIKE 'Walk-in (counter) — %');";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWalkIn", table: "Bookings", nullable: false, defaultValue: false);
            migrationBuilder.Sql(BackfillSql);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "IsWalkIn", table: "Bookings");
        }
    }
}
