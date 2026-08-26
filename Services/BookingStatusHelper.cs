using CourtBookingSystem.Data;
using CourtBookingSystem.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services
{
    // Simplified booking workflow helper.
    //
    // The status set is intentionally minimal (see Documentation/WORKFLOW.md):
    //   Pending → ForApproval → Confirmed → Completed
    //                                    ↘ Cancelled
    //
    // "Booking date passes" is modelled as an on-read auto-transition rather
    // than a background job to keep deployment simple for small/medium centers.
    // Dashboards and listings call AutoCompletePastBookingsAsync so any
    // Confirmed booking whose end time is in the past flips to Completed.
    public static class BookingStatusHelper
    {
        public static async Task<int> AutoCompletePastBookingsAsync(ApplicationDbContext db)
        {
            var now = PhilippineTime.Now;
            var nowDate = now.Date;
            var nowTime = now.TimeOfDay;

            // First sweep: any Confirmed booking on a previous day.
            var pastDayConfirmed = await db.Bookings
                .Where(b => b.BookingStatus == BookingStatus.Confirmed && b.BookingDate < nowDate)
                .ToListAsync();

            // Second sweep: today's Confirmed bookings whose end time has passed.
            var endedToday = await db.Bookings
                .Where(b => b.BookingStatus == BookingStatus.Confirmed && b.BookingDate == nowDate)
                .ToListAsync();
            var todayPast = endedToday.Where(b => b.EndTime <= nowTime).ToList();

            var changed = 0;
            foreach (var b in pastDayConfirmed)
            {
                b.BookingStatus = BookingStatus.Completed;
                changed++;
            }
            foreach (var b in todayPast)
            {
                b.BookingStatus = BookingStatus.Completed;
                changed++;
            }

            if (changed > 0)
            {
                await db.SaveChangesAsync();
            }
            return changed;
        }
    }
}
