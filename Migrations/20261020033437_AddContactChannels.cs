using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBookingSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddContactChannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EventsContactName",
                table: "AboutContents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventsPhone",
                table: "AboutContents",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookName",
                table: "AboutContents",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookUrl",
                table: "AboutContents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramHandle",
                table: "AboutContents",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstagramUrl",
                table: "AboutContents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchedulingContactName",
                table: "AboutContents",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // Populate existing installations once. Later CMS edits are retained.
            migrationBuilder.Sql(@"
                UPDATE AboutContents SET
                    SchedulingContactName = 'Jenny',
                    EventsPhone = '0915 619 0673',
                    EventsContactName = 'Catrina',
                    FacebookName = 'Campo Cebu',
                    FacebookUrl = 'https://www.facebook.com/share/1Eo5wmywbV/?mibextid=wwXIfr',
                    InstagramHandle = '@campocebu',
                    InstagramUrl = 'https://www.instagram.com/campocebu/';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EventsContactName",
                table: "AboutContents");

            migrationBuilder.DropColumn(
                name: "EventsPhone",
                table: "AboutContents");

            migrationBuilder.DropColumn(
                name: "FacebookName",
                table: "AboutContents");

            migrationBuilder.DropColumn(
                name: "FacebookUrl",
                table: "AboutContents");

            migrationBuilder.DropColumn(
                name: "InstagramHandle",
                table: "AboutContents");

            migrationBuilder.DropColumn(
                name: "InstagramUrl",
                table: "AboutContents");

            migrationBuilder.DropColumn(
                name: "SchedulingContactName",
                table: "AboutContents");
        }
    }
}
