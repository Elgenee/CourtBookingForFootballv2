using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.Models
{
    // Defines the operating hours of the facility, one row per day of week.
    public class BusinessHour
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Day of Week")]
        public DayOfWeek DayOfWeek { get; set; }

        [Required]
        [Display(Name = "Open Time")]
        public TimeSpan OpenTime { get; set; }

        [Required]
        [Display(Name = "Close Time")]
        public TimeSpan CloseTime { get; set; }

        [Display(Name = "Is Closed")]
        public bool IsClosed { get; set; } = false;
    }
}
