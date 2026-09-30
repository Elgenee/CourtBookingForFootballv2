using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;

namespace CourtBookingSystem.Services
{
    public static class PaymentSummaryHelper
    {
        public static PaymentSummary Calculate(Booking booking)
        {
            var amountPaid = booking.Payments?
                .Where(p => p.PaymentStatus == PaymentStatus.Approved)
                .Sum(p => p.Amount) ?? 0m;

            amountPaid = decimal.Round(amountPaid, 2, MidpointRounding.AwayFromZero);
            var remaining = decimal.Round(booking.TotalAmount - amountPaid, 2, MidpointRounding.AwayFromZero);
            if (remaining < 0m) remaining = 0m;

            return new PaymentSummary(booking.TotalAmount, amountPaid, remaining);
        }
    }

    public sealed record PaymentSummary(decimal TotalAmount, decimal AmountPaid, decimal RemainingBalance);
}
