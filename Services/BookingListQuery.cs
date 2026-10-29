using CourtBookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services;

public static class BookingListQuery
{
    public static async Task<List<Booking>> LoadAsync(IQueryable<Booking> query, string? paymentState)
    {
        var ordered = query.AsNoTracking().OrderByDescending(b => b.BookingDate);
        if (string.IsNullOrEmpty(paymentState))
            return await ordered.Take(200).ToListAsync();

        if (paymentState is not ("Unpaid" or "PartiallyPaid" or "FullyPaid"))
            return new List<Booking>();

        // Use the same approved-only decimal calculation as booking details.
        // Streaming supports SQLite (which cannot SUM decimal amounts in EF8)
        // and applies the 200-result cap after matching the payment state.
        var results = new List<Booking>();
        await foreach (var booking in ordered.AsAsyncEnumerable())
        {
            if (PaymentSummaryHelper.Calculate(booking).Status != paymentState) continue;
            results.Add(booking);
            if (results.Count == 200) break;
        }
        return results;
    }
}
