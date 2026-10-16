using CourtBookingSystem.Models;

namespace CourtBookingSystem.ViewModels;

public class DailyBookingCalendarViewModel
{
    public DateTime Date { get; init; }
    public DateTime Month => new(Date.Year, Date.Month, 1);
    public int? CourtId { get; init; }
    public List<Court> Courts { get; init; } = new();
    public List<DailyCalendarDay> Days { get; init; } = new();
    public List<DailyCalendarSlot> Slots { get; init; } = new();
    public string? EmptyMessage { get; init; }
    public int AvailableHours => Slots.Count(s => s.Status == "available");
    public int BookedHours => Slots.Count(s => s.Status == "booked");
}

public record DailyCalendarDay(DateTime Date, bool IsCurrentMonth, bool IsClosed);

public record DailyCalendarSlot(
    DateTime Start,
    DateTime End,
    string Status,
    string Label,
    string? Description,
    decimal? HourlyRate)
{
    public IReadOnlyList<DailyCalendarBooking> Bookings { get; init; } = Array.Empty<DailyCalendarBooking>();
}

public record DailyCalendarBooking(int Id, string CustomerName);
