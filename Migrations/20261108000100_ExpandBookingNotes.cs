using Microsoft.EntityFrameworkCore.Migrations;

namespace CourtBookingSystem.Migrations
{
    public partial class ExpandBookingNotes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite already stores notes as unrestricted TEXT. SQL Server needs
            // nvarchar(max) for the application's new 5,000-character limit.
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.AlterColumn<string>(
                    name: "Notes",
                    table: "Bookings",
                    type: "nvarchar(max)",
                    maxLength: 5000,
                    nullable: true,
                    oldClrType: typeof(string),
                    oldType: "nvarchar(500)",
                    oldMaxLength: 500,
                    oldNullable: true);
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.AlterColumn<string>(
                    name: "Notes",
                    table: "Bookings",
                    type: "nvarchar(500)",
                    maxLength: 500,
                    nullable: true,
                    oldClrType: typeof(string),
                    oldType: "nvarchar(max)",
                    oldMaxLength: 5000,
                    oldNullable: true);
            }
        }
    }
}
