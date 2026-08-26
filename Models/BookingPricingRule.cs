using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CourtBookingSystem.Models
{
    public class BookingPricingRule
    {
        public int Id { get; set; }

        [Required]
        public int CourtId { get; set; }
        public Court? Court { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public bool IsPromotional { get; set; }

        public bool IsEnabled { get; set; } = true;

        [DataType(DataType.Date)]
        public DateTime? EffectiveStartDate { get; set; }

        [DataType(DataType.Date)]
        public DateTime? EffectiveEndDate { get; set; }

        [Required]
        [DataType(DataType.Time)]
        public TimeSpan StartTime { get; set; }

        [Required]
        [DataType(DataType.Time)]
        public TimeSpan EndTime { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Range(0, 999999)]
        public decimal HourlyRate { get; set; }

        public int Priority { get; set; }

        public int DisplayOrder { get; set; }
    }
}
