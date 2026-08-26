using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services
{
    // Computes available time slots for booking.
    // Used by both the public Bookings flow (one-court view) and the Find Availability page (cross-court).
    //
    // Group-aware availability: a slot is unavailable not only when the court
    // itself has an overlapping booking/block, but also when a sibling court
    // in the same CourtGroup constrains it. See CourtGroupAvailabilityService.
    public class BookingAvailabilityService
    {
        private readonly ApplicationDbContext _db;
        private readonly CourtGroupAvailabilityService _groups;
        private readonly BookingPricingService _pricing;

        public BookingAvailabilityService(ApplicationDbContext db, CourtGroupAvailabilityService groups, BookingPricingService pricing)
        {
            _db = db;
            _groups = groups;
            _pricing = pricing;
        }

        public record TimeSlot(DateTime StartDateTime, DateTime EndDateTime, bool IsAvailable, string? Reason)
        {
            public TimeSpan Start => StartDateTime.TimeOfDay;
            public TimeSpan End => EndDateTime.TimeOfDay;
        }
        public record AvailableSlotRow(
            int CourtId,
            string CourtName,
            SportType SportType,
            DateTime Date,
            TimeSpan StartTime,
            TimeSpan EndTime,
            decimal HourlyRate,
            decimal TotalAmount,
            bool IsPromoRate,
            bool IsMixedRate);

        // For one specific court — used by Bookings/Create slot grid.
        public async Task<List<TimeSlot>> GenerateSlotsForCourtAsync(int courtId, DateTime date, int durationHours)
        {
            var slots = new List<TimeSlot>();
            var bh = await _db.BusinessHours.FirstOrDefaultAsync(b => b.DayOfWeek == date.DayOfWeek);
            if (bh == null || bh.IsClosed) return slots;

            var existing = await SameDayBookingsAsync(courtId, date);
            var blocked = await SameDayBlockedAsync(courtId, date);
            var groupConflicts = await _groups.GetGroupConflictsAsync(courtId, date);

            var now = PhilippineTime.Now;
            var duration = TimeSpan.FromHours(durationHours);
            var operatingRange = OperatingHoursHelper.GetOperatingRange(date, bh);

            for (var slotStart = operatingRange.Start; slotStart.Add(duration) <= operatingRange.End; slotStart = slotStart.AddHours(1))
            {
                var slotEnd = slotStart.Add(duration);
                bool isPast = slotStart < now;
                bool clashBooking = existing.Any(b => OperatingHoursHelper.Overlaps(b.StartDateTime, b.EndDateTime, slotStart, slotEnd));
                bool clashBlocked = blocked.Any(s => OperatingHoursHelper.Overlaps(s.StartDateTime, s.EndDateTime, slotStart, slotEnd));
                var groupClash = CourtGroupAvailabilityService.FindOverlap(groupConflicts, slotStart, slotEnd);

                string? reason =
                    isPast        ? "Past"
                    : clashBooking ? "Booked"
                    : clashBlocked ? "Blocked"
                    : groupClash != null ? groupClash.Reason
                    : null;

                slots.Add(new TimeSlot(slotStart, slotEnd, reason == null, reason));
            }
            return slots;
        }

        // For all active courts of a given sport — used by Find Availability page.
        public async Task<List<AvailableSlotRow>> FindAvailableAsync(SportType sport, DateTime date, int durationHours)
        {
            var results = new List<AvailableSlotRow>();

            var bh = await _db.BusinessHours.FirstOrDefaultAsync(b => b.DayOfWeek == date.DayOfWeek);
            if (bh == null || bh.IsClosed) return results;

            var courts = await _db.Courts
                .Where(c => c.IsActive
                    && c.SportType == sport
                    && c.Status != CourtStatus.Closed
                    && c.Status != CourtStatus.UnderMaintenance)
                .OrderBy(c => c.CourtName)
                .ToListAsync();
            if (courts.Count == 0) return results;

            // Materialize same-operating-day data once per court (still cross-provider safe).
            var now = PhilippineTime.Now;
            var duration = TimeSpan.FromHours(durationHours);
            var operatingRange = OperatingHoursHelper.GetOperatingRange(date, bh);

            foreach (var court in courts)
            {
                var existing = await SameDayBookingsAsync(court.Id, date);
                var blocked = await SameDayBlockedAsync(court.Id, date);
                var groupConflicts = await _groups.GetGroupConflictsAsync(court.Id, date);

                for (var slotStart = operatingRange.Start; slotStart.Add(duration) <= operatingRange.End; slotStart = slotStart.AddHours(1))
                {
                    var slotEnd = slotStart.Add(duration);
                    bool isPast = slotStart < now;
                    bool clashBooking = existing.Any(b => OperatingHoursHelper.Overlaps(b.StartDateTime, b.EndDateTime, slotStart, slotEnd));
                    bool clashBlocked = blocked.Any(s => OperatingHoursHelper.Overlaps(s.StartDateTime, s.EndDateTime, slotStart, slotEnd));
                    bool clashGroup = CourtGroupAvailabilityService.FindOverlap(groupConflicts, slotStart, slotEnd) != null;
                    if (isPast || clashBooking || clashBlocked || clashGroup) continue;

                    var price = _pricing.Quote(court, date, slotStart.TimeOfDay, durationHours);
                    results.Add(new AvailableSlotRow(
                        court.Id, court.CourtName, court.SportType,
                        date.Date, slotStart.TimeOfDay, slotEnd.TimeOfDay,
                        price.HourlyRate, price.TotalAmount, price.IsPromoApplied, price.IsMixedRate));
                }
            }

            // Sort: earliest first across all courts; secondary by court name.
            return results
                .OrderBy(r => OperatingHoursHelper.ToOperatingDateTime(date, bh, r.StartTime))
                .ThenBy(r => r.CourtName)
                .ToList();
        }

        private async Task<List<(DateTime StartDateTime, DateTime EndDateTime)>> SameDayBookingsAsync(int courtId, DateTime date)
        {
            var bh = await _db.BusinessHours.FirstOrDefaultAsync(b => b.DayOfWeek == date.DayOfWeek);
            if (bh == null) return new List<(DateTime StartDateTime, DateTime EndDateTime)>();

            var rows = await _db.Bookings
                .Where(b => b.CourtId == courtId
                    && b.BookingDate.Date == date.Date
                    && b.BookingStatus != BookingStatus.Cancelled)
                .Select(b => new { b.StartTime, b.EndTime })
                .ToListAsync();
            return rows
                .Select(r => OperatingHoursHelper.GetTimeWindow(date, bh, r.StartTime, r.EndTime))
                .ToList();
        }

        private async Task<List<(DateTime StartDateTime, DateTime EndDateTime)>> SameDayBlockedAsync(int courtId, DateTime date)
        {
            var bh = await _db.BusinessHours.FirstOrDefaultAsync(b => b.DayOfWeek == date.DayOfWeek);
            if (bh == null) return new List<(DateTime StartDateTime, DateTime EndDateTime)>();

            var rows = await _db.BlockedSlots
                .Where(s => s.CourtId == courtId && s.BlockedDate.Date == date.Date)
                .Select(s => new { s.StartTime, s.EndTime })
                .ToListAsync();
            return rows
                .Select(r => OperatingHoursHelper.GetTimeWindow(date, bh, r.StartTime, r.EndTime))
                .ToList();
        }
    }
}
