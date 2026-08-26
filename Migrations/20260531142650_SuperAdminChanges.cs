using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBookingSystem.Migrations
{
    /// <inheritdoc />
    public partial class SuperAdminChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AboutContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FacilitiesList = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ContactPhone = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Location = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    OpeningHoursDays = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    OpeningHoursTimes = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AboutContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GalleryImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImagePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    Caption = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GalleryImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HeroImages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImagePath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeroImages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebsiteSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WebsiteName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    WebsiteTagline = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    NavbarLogoPath = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    HeroTitle = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    HeroSubtitle = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    HeroButtonText = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AboutContents");

            migrationBuilder.DropTable(
                name: "GalleryImages");

            migrationBuilder.DropTable(
                name: "HeroImages");

            migrationBuilder.DropTable(
                name: "WebsiteSettings");
        }
    }
}
