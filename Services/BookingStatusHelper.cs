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
    // "Booking end passes" is modelled as an on-read auto-transition rather
    // than a background job to keep deployment simple for small/medium centers.
    // Dashboards and listings call AutoCompletePastBookingsAsync so any
    // Confirmed booking whose end time is in the past flips to Completed.
    public static class BookingStatusHelper
    {
        public static async Task<int> AutoCompletePastBookingsAsync(ApplicationDbContext db, TimeProvider? timeProvider = null)
        {
            var now = PhilippineTime.FromUtc((timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime);
            var tomorrow = now.Date.AddDays(1);

            // Keep TimeSpan calculations in memory for SQL Server/SQLite compatibility.
            var candidates = await db.Bookings
                .Where(b => b.BookingStatus == BookingStatus.Confirmed && b.BookingDate < tomorrow)
                .ToListAsync();

            if (candidates.Count == 0)
            {
                return 0;
            }

            var businessHours = await db.BusinessHours
                .AsNoTracking()
                .ToDictionaryAsync(h => h.DayOfWeek);

            var changed = 0;
            foreach (var b in candidates)
            {
                // BookingDate is the operating date: after-midnight starts can
                // belong to the next calendar day, just as during booking creation.
                var start = businessHours.TryGetValue(b.BookingDate.DayOfWeek, out var hours)
                    ? OperatingHoursHelper.ToOperatingDateTime(b.BookingDate, hours, b.StartTime)
                    : b.BookingDate.Date.Add(b.StartTime);

                // 11 PM to 12 AM ends at midnight on the following day.
                var end = start.Add(OperatingHoursHelper.GetDuration(b.StartTime, b.EndTime));
                if (end <= now)
                {
                    b.BookingStatus = BookingStatus.Completed;
                    changed++;
                }
            }

            if (changed > 0)
            {
                await db.SaveChangesAsync();
            }
            return changed;
        }
    }
}
