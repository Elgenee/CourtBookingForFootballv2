using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.Models
{
    // Notification visible to Admin and Staff.
    // Shared across all admin/staff users — when any one opens it, it's marked read for everyone.
    public class Notification
    {
        public int Id { get; set; }

        [Required, StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required, StringLength(500)]
        public string Message { get; set; } = string.Empty;

        public int? RelatedBookingId { get; set; }

        // Relative URL the notification opens (e.g. "/Admin/Payments").
        [StringLength(500)]
        public string? Link { get; set; }

        public bool IsRead { get; set; } = false;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public DateTime? ReadDate { get; set; }
    }
}
