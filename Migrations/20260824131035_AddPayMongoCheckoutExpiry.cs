using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBookingSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPayMongoCheckoutExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CheckoutExpiresAtUtc",
                table: "Payments",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckoutExpiresAtUtc",
                table: "Payments");
        }
    }
}
