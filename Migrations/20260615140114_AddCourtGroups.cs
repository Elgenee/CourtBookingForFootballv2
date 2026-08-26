using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBookingSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddCourtGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CourtGroupId",
                table: "Courts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFullCourt",
                table: "Courts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CourtGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourtGroups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Courts_CourtGroupId",
                table: "Courts",
                column: "CourtGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CourtGroups_GroupCode",
                table: "CourtGroups",
                column: "GroupCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Courts_CourtGroups_CourtGroupId",
                table: "Courts",
                column: "CourtGroupId",
                principalTable: "CourtGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courts_CourtGroups_CourtGroupId",
                table: "Courts");

            migrationBuilder.DropTable(
                name: "CourtGroups");

            migrationBuilder.DropIndex(
                name: "IX_Courts_CourtGroupId",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "CourtGroupId",
                table: "Courts");

            migrationBuilder.DropColumn(
                name: "IsFullCourt",
                table: "Courts");
        }
    }
}
