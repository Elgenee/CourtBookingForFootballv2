using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.Models
{
    // Extends the default IdentityUser with extra profile fields.
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [StringLength(100)]
        public string FullName { get; set; } = string.Empty;

        [StringLength(20)]
        public string? MobileNumber { get; set; }

        [StringLength(250)]
        public string? Address { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    }
}
