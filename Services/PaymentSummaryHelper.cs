using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;

namespace CourtBookingSystem.Services
{
    public static class PaymentSummaryHelper
    {
        public static bool CanCollectBalance(Booking booking) =>
            booking.BookingStatus is BookingStatus.Confirmed or BookingStatus.PartiallyPaid or BookingStatus.Completed
            && Calculate(booking) is { AmountPaid: > 0m, RemainingBalance: > 0m };

        public static decimal PaidVia(Booking booking, PaymentMethod method) => booking.Payments
            .Where(p => p.PaymentStatus == PaymentStatus.Approved && p.PaymentMethod == method)
            .Sum(p => p.Amount);

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

    public sealed record PaymentSummary(decimal TotalAmount, decimal AmountPaid, decimal RemainingBalance)
    {
        public string Status => AmountPaid <= 0m ? "Unpaid" : RemainingBalance > 0m ? "PartiallyPaid" : "FullyPaid";
        public string StatusLabel => Status switch
        {
            "PartiallyPaid" => "Partially Paid",
            "FullyPaid" => "Fully Paid",
            _ => "Unpaid"
        };
    }
}
