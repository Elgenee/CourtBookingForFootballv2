namespace CourtBookingSystem.Models.Enums
{
    // Simplified booking workflow (see Documentation/WORKFLOW.md).
    // Numeric values are persisted to the database — do not reuse or
    // reorder existing values without writing a data migration.
    public enum BookingStatus
    {
        Pending = 1,      // Booking created — waiting for customer payment proof
        ForApproval = 2,  // Customer uploaded GCash proof — waiting for Staff/Admin review
        Confirmed = 3,    // Payment approved — booking confirmed
        Completed = 4,    // Booking schedule finished
        Cancelled = 5,    // Cancelled by customer or Staff/Admin
        PartiallyPaid = 6  // Reservation payment received — balance remains
    }
}
