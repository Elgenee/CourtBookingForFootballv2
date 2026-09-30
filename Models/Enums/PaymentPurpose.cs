namespace CourtBookingSystem.Models.Enums
{
    // Identifies what a payment record is intended to pay for.
    // Numeric values are persisted to the database; append new values only.
    public enum PaymentPurpose
    {
        FullPayment = 1,
        Reservation = 2,
        Balance = 3
    }
}
