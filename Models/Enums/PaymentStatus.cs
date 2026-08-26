namespace CourtBookingSystem.Models.Enums
{
    // Simplified payment workflow (see Documentation/WORKFLOW.md).
    // Numeric values are persisted to the database — do not reuse or
    // reorder existing values without writing a data migration.
    public enum PaymentStatus
    {
        Unpaid = 1,    // No payment proof uploaded yet
        Submitted = 2, // Customer uploaded payment proof
        Approved = 3,  // Payment verified by Staff/Admin
        Rejected = 4   // Payment proof rejected
    }
}
