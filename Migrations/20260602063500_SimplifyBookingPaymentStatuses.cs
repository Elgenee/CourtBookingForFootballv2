using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBookingSystem.Migrations
{
    /// <summary>
    /// Data migration — remaps existing BookingStatus / PaymentStatus enum
    /// integer values to the simplified workflow defined in
    /// Documentation/WORKFLOW.md.
    ///
    /// BookingStatus (old → new int):
    ///   1 PendingPayment   → 1 Pending
    ///   2 PaymentSubmitted → 2 ForApproval
    ///   3 Confirmed        → 3 Confirmed       (unchanged)
    ///   4 CheckedIn        → 3 Confirmed       (merged — manual check-in removed)
    ///   5 Completed        → 4 Completed
    ///   6 Cancelled        → 5 Cancelled
    ///   7 NoShow           → 5 Cancelled       (merged — no-show treated as cancelled)
    ///
    /// PaymentStatus (old → new int):
    ///   1 Unpaid     → 1 Unpaid     (unchanged)
    ///   2 Submitted  → 2 Submitted  (unchanged)
    ///   3 Verified   → 3 Approved   (rename only — int unchanged)
    ///   4 Rejected   → 4 Rejected   (unchanged)
    ///   5 Refunded   → 4 Rejected   (merged — refunds out of simplified scope)
    ///
    /// No schema changes — column types remain INTEGER/int. CASE expressions
    /// are used so the migration is idempotent and works on SQLite, SQL Server,
    /// PostgreSQL and MySQL providers.
    /// </summary>
    public partial class SimplifyBookingPaymentStatuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE Bookings
                SET BookingStatus = CASE BookingStatus
                    WHEN 1 THEN 1   -- PendingPayment   -> Pending
                    WHEN 2 THEN 2   -- PaymentSubmitted -> ForApproval
                    WHEN 3 THEN 3   -- Confirmed        -> Confirmed
                    WHEN 4 THEN 3   -- CheckedIn        -> Confirmed
                    WHEN 5 THEN 4   -- Completed        -> Completed
                    WHEN 6 THEN 5   -- Cancelled        -> Cancelled
                    WHEN 7 THEN 5   -- NoShow           -> Cancelled
                    ELSE BookingStatus
                END
                WHERE BookingStatus IN (1,2,3,4,5,6,7);
            ");

            migrationBuilder.Sql(@"
                UPDATE Payments
                SET PaymentStatus = CASE PaymentStatus
                    WHEN 1 THEN 1   -- Unpaid     -> Unpaid
                    WHEN 2 THEN 2   -- Submitted  -> Submitted
                    WHEN 3 THEN 3   -- Verified   -> Approved
                    WHEN 4 THEN 4   -- Rejected   -> Rejected
                    WHEN 5 THEN 4   -- Refunded   -> Rejected
                    ELSE PaymentStatus
                END
                WHERE PaymentStatus IN (1,2,3,4,5);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down is best-effort: merged statuses (CheckedIn vs Confirmed,
            // NoShow vs Cancelled, Refunded vs Rejected) cannot be recovered
            // losslessly. We restore the canonical numeric layout so the
            // legacy enum compiles, mapping the merged buckets back to their
            // most-likely original value.
            migrationBuilder.Sql(@"
                UPDATE Bookings
                SET BookingStatus = CASE BookingStatus
                    WHEN 1 THEN 1   -- Pending     -> PendingPayment
                    WHEN 2 THEN 2   -- ForApproval -> PaymentSubmitted
                    WHEN 3 THEN 3   -- Confirmed   -> Confirmed
                    WHEN 4 THEN 5   -- Completed   -> Completed
                    WHEN 5 THEN 6   -- Cancelled   -> Cancelled
                    ELSE BookingStatus
                END
                WHERE BookingStatus IN (1,2,3,4,5);
            ");

            // PaymentStatus int values 1–4 remain valid in the legacy enum,
            // so no rewrite is necessary on Down(). Refunded (5) is lost.
        }
    }
}
