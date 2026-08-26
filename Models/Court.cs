using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CourtBookingSystem.Models.Enums;

namespace CourtBookingSystem.Models
{
    public class Court
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        [Display(Name = "Court Name")]
        public string CourtName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Sport Type")]
        public SportType SportType { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Hourly Rate")]
        [Range(0, 999999)]
        public decimal HourlyRate { get; set; }

        [Display(Name = "Enable Promo Rate")]
        public bool IsPromoRateEnabled { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Promo Hourly Rate")]
        [Range(0, 999999)]
        public decimal? PromoHourlyRate { get; set; }

        [DataType(DataType.Time)]
        [Display(Name = "Promo Start Time")]
        public TimeSpan? PromoStartTime { get; set; }

        [DataType(DataType.Time)]
        [Display(Name = "Promo End Time")]
        public TimeSpan? PromoEndTime { get; set; }

        public int PromoDayMask { get; set; }

        [Required]
        public CourtStatus Status { get; set; } = CourtStatus.Available;

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        // ----- Court Group / shared physical area -----
        // Null = independent court (no sharing).
        [Display(Name = "Court Group")]
        public int? CourtGroupId { get; set; }
        public CourtGroup? CourtGroup { get; set; }

        // A "Full Court" occupies the entire shared area in its group, so
        // booking it makes every sibling unavailable. A non-full ("Split")
        // court only blocks the Full Court(s) in the group when booked.
        [Display(Name = "Is Full Court")]
        public bool IsFullCourt { get; set; }

        // Navigation
        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
        public ICollection<BlockedSlot> BlockedSlots { get; set; } = new List<BlockedSlot>();
    }
}
