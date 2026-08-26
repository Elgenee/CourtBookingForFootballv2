using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBookingSystem.Models
{
    // A manually blocked time range on a court (maintenance, private event, etc.)
    public class BlockedSlot
    {
        public int Id { get; set; }

        [Required]
        public int CourtId { get; set; }

        [ForeignKey(nameof(CourtId))]
        public Court? Court { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Blocked Date")]
        public DateTime BlockedDate { get; set; }

        [Required]
        [Display(Name = "Start Time")]
        public TimeSpan StartTime { get; set; }

        [Required]
        [Display(Name = "End Time")]
        public TimeSpan EndTime { get; set; }

        [StringLength(250)]
        public string? Reason { get; set; }

        [Display(Name = "Created Date")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
