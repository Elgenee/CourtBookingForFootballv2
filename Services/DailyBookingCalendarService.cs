using CourtBookingSystem.Data;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services;

// Presentation-only reads. In particular, do not auto-complete bookings here:
// even historical calendar requests must leave persisted statuses untouched.
public class DailyBookingCalendarService
{
    private readonly ApplicationDbContext _db;
    private readonly BookingAvailabilityService _availability;
    private readonly CourtGroupAvailabilityService _groups;
    private readonly BookingPricingService _pricing;

    public DailyBookingCalendarService(ApplicationDbContext db, BookingAvailabilityService availability,
        CourtGroupAvailabilityService groups, BookingPricingService pricing)
    {
        _db = db;
        _availability = availability;
        _groups = groups;
        _pricing = pricing;
    }

    public async Task<DailyBookingCalendarViewModel> BuildAsync(DateTime date, int? courtId)
    {
        date = date.Date;
        var courts = await _db.Courts.AsNoTracking().OrderBy(c => c.CourtName).ToListAsync();
        var court = courtId.HasValue ? courts.SingleOrDefault(c => c.Id == courtId) : courts.FirstOrDefault();
        if (courtId.HasValue && court == null)
            throw new ArgumentException("The selected field does not exist.", nameof(courtId));

        var hoursByDay = await _db.BusinessHours.AsNoTracking().ToDictionaryAsync(h => h.DayOfWeek);
        var month = new DateTime(date.Year, date.Month, 1);
        var first = month.AddDays(-(int)month.DayOfWeek);
        var last = month.AddMonths(1).AddDays(-1);
        last = last.AddDays(6 - (int)last.DayOfWeek);
        var days = new List<DailyCalendarDay>();
        for (var day = first; day <= last; day = day.AddDays(1))
            days.Add(new(day, day.Month == month.Month && day.Year == month.Year,
                !hoursByDay.TryGetValue(day.DayOfWeek, out var dayHours) || dayHours.IsClosed));

        var slots = new List<DailyCalendarSlot>();
        string? emptyMessage = null;
        if (court == null)
            emptyMessage = "No fields have been configured yet.";
        else if (!hoursByDay.TryGetValue(date.DayOfWeek, out var hours) || hours.IsClosed)
            emptyMessage = "The facility is closed on this operating date.";
        else
        {
            // Generate the same one-hour windows and availability reasons as the
            // customer flow. Resolve booking names separately, including past hours.
            var generated = await _availability.GenerateSlotsForCourtAsync(court.Id, date, 1);
            var bookings = await _db.Bookings.AsNoTracking()
                .Where(b => b.CourtId == court.Id && b.BookingDate.Date == date
                    && b.BookingStatus != BookingStatus.Cancelled)
                .Select(b => new { b.Id, b.StartTime, b.EndTime, b.CustomerName, b.BookingStatus })
                .ToListAsync();
            var bookingWindows = bookings.Select(b => new
            {
                Window = OperatingHoursHelper.GetTimeWindow(date, hours, b.StartTime, b.EndTime),
                b.Id,
                b.CustomerName,
                b.BookingStatus
            }).ToList();
            var blocks = await _db.BlockedSlots.AsNoTracking()
                .Where(b => b.CourtId == court.Id && b.BlockedDate.Date == date)
                .Select(b => new { b.StartTime, b.EndTime, b.Reason }).ToListAsync();
            var blockWindows = blocks.Select(b => new
            {
                Window = OperatingHoursHelper.GetTimeWindow(date, hours, b.StartTime, b.EndTime),
                b.Reason
            }).ToList();
            var groupConflicts = await _groups.GetGroupConflictsAsync(court.Id, date);
            var fieldUnavailable = !court.IsActive || court.Status is CourtStatus.Closed or CourtStatus.UnderMaintenance;

            foreach (var slot in generated)
            {
                var reservations = bookingWindows.Where(b => OperatingHoursHelper.Overlaps(
                    b.Window.Start, b.Window.End, slot.StartDateTime, slot.EndDateTime))
                    .OrderBy(b => b.Window.Start).ThenBy(b => b.Id).ToList();
                var block = blockWindows.FirstOrDefault(b => OperatingHoursHelper.Overlaps(
                    b.Window.Start, b.Window.End, slot.StartDateTime, slot.EndDateTime));
                var group = CourtGroupAvailabilityService.FindOverlap(groupConflicts, slot.StartDateTime, slot.EndDateTime);

                // Existing reservations remain visible when looking at historical
                // dates, even though the public service reports those slots as Past.
                if (reservations.Count > 0)
                    slots.Add(new(slot.StartDateTime, slot.EndDateTime, "booked",
                        string.Join(" / ", reservations.Select(b => string.IsNullOrWhiteSpace(b.CustomerName) ? "Booked" : b.CustomerName).Distinct()),
                        string.Join(", ", reservations.Select(b => b.BookingStatus.ToString()).Distinct()), null)
                    {
                        Bookings = reservations.Select(b => new DailyCalendarBooking(b.Id,
                            string.IsNullOrWhiteSpace(b.CustomerName) ? "Booked" : b.CustomerName)).ToArray()
                    });
                else if (block != null)
                    slots.Add(new(slot.StartDateTime, slot.EndDateTime, "blocked", "Blocked", block.Reason, null));
                else if (group != null)
                    slots.Add(new(slot.StartDateTime, slot.EndDateTime, "unavailable", "Unavailable", group.Reason, null));
                else if (fieldUnavailable)
                    slots.Add(new(slot.StartDateTime, slot.EndDateTime, "unavailable", "Unavailable",
                        !court.IsActive ? "Field inactive" : court.Status == CourtStatus.UnderMaintenance ? "Under maintenance" : "Field closed", null));
                else if (!slot.IsAvailable)
                    slots.Add(new(slot.StartDateTime, slot.EndDateTime, "unavailable",
                        slot.Reason == "Past" ? "Past" : "Unavailable", slot.Reason, null));
                else
                    slots.Add(new(slot.StartDateTime, slot.EndDateTime, "available", "Available", null,
                        _pricing.Quote(court, date, slot.Start, 1).HourlyRate));
            }

            if (slots.Count == 0)
                emptyMessage = "No complete hourly slots are configured for this operating date.";
        }

        return new()
        {
            Date = date,
            CourtId = court?.Id,
            Courts = courts,
            Days = days,
            Slots = slots,
            EmptyMessage = emptyMessage
        };
    }
}
