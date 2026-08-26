using CourtBookingSystem.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBookingSystem.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260717153500_AddLandingColorScheme")]
    public partial class AddLandingColorScheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LandingColorScheme",
                table: "WebsiteSettings",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "royal-blue");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LandingColorScheme",
                table: "WebsiteSettings");
        }
    }
}
