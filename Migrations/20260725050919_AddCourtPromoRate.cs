using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBookingSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddCourtPromoRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPromoRateEnabled",
                table: "Courts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PromoDayMask",
                table: "Courts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "PromoEndTime",
                table: "Courts",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PromoHourlyRate",
                table: "Courts",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "PromoStartTime",
                table: "Courts",
                type: "time",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPromoRateEnabled",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "PromoDayMask",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "PromoEndTime",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "PromoHourlyRate",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "PromoStartTime",
                table: "Courts");
        }
    }
}
