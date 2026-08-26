using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CourtBookingSystem.Models.Enums;

namespace CourtBookingSystem.Models
{
    public class Booking
    {
        public int Id { get; set; }

        [Required]
        [StringLength(30)]
        [Display(Name = "Booking Reference No.")]
        public string BookingReferenceNo { get; set; } = string.Empty;

        // Nullable to support Guest bookings (no login required).
        public string? UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public ApplicationUser? User { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Customer Name")]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        [EmailAddress]
        [Display(Name = "Customer Email")]
        public string CustomerEmail { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        [Phone]
        [Display(Name = "Customer Mobile")]
        public string CustomerMobile { get; set; } = string.Empty;

        [Required]
        public int CourtId { get; set; }

        [ForeignKey(nameof(CourtId))]
        public Court? Court { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Booking Date")]
        public DateTime BookingDate { get; set; }

        [Required]
        [Display(Name = "Start Time")]
        public TimeSpan StartTime { get; set; }

        [Required]
        [Display(Name = "End Time")]
        public TimeSpan EndTime { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Total Amount")]
        public decimal TotalAmount { get; set; }

        [Required]
        [Display(Name = "Booking Status")]
        public BookingStatus BookingStatus { get; set; } = BookingStatus.Pending;

        [StringLength(500)]
        public string? Notes { get; set; }

        [Display(Name = "Created Date")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
