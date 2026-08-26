using CourtBookingSystem.Data;
using CourtBookingSystem.Models;

namespace CourtBookingSystem.Services
{
    // Centralized helper for creating notifications.
    // Notifications are shared between Admin and Staff users (single Notifications table).
    public static class NotificationService
    {
        public static async Task NotifyAsync(
            ApplicationDbContext db,
            string title,
            string message,
            string? link = null,
            int? relatedBookingId = null)
        {
            db.Notifications.Add(new Notification
            {
                Title = title,
                Message = message,
                Link = link,
                RelatedBookingId = relatedBookingId,
                IsRead = false,
                CreatedDate = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
    }
}
