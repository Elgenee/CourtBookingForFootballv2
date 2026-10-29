using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBookingSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddCashBalanceCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CollectionSubmissionId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CollectionSubmissionId",
                table: "Payments",
                column: "CollectionSubmissionId",
                unique: true,
                filter: "[CollectionSubmissionId] IS NOT NULL");

            // Payment state is derived from approved transactions. A paid
            // reservation confirms the booking even while a balance is due.
            migrationBuilder.Sql(@"
                UPDATE Bookings SET BookingStatus = 3 WHERE BookingStatus = 6
                    AND EXISTS (SELECT 1 FROM Payments WHERE Payments.BookingId = Bookings.Id AND PaymentStatus = 3);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_CollectionSubmissionId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CollectionSubmissionId",
                table: "Payments");
        }
    }
}
