namespace CourtBookingSystem.Services;

public static class CustomerBookingRules
{
    public const string AdvanceBookingReason = "Requires booking at least 4 hours in advance.";
    public const string AdvanceBookingMessage = "Late-night bookings (12 AM–4 AM) must be made at least 4 hours in advance.";

    // Both arguments are full Philippine dates/times, after resolving the operating date.
    // Include 4:00 AM; later starts are outside the late-night window.
    public static DateTime? GetAdvanceBookingDeadline(DateTime slotStart) =>
        slotStart.TimeOfDay <= TimeSpan.FromHours(4) ? slotStart.AddHours(-4) : null;

    public static bool CanBook(DateTime slotStart, DateTime now) =>
        GetAdvanceBookingDeadline(slotStart) is not DateTime deadline || now <= deadline;
}
