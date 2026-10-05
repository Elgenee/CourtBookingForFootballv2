using System.ComponentModel.DataAnnotations;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;

namespace CourtBookingSystem.ViewModels
{
    public class BookingFormViewModel
    {
        [Required(ErrorMessage = "Please choose a field.")]
        [Display(Name = "Field")]
        public int CourtId { get; set; }

        [Required(ErrorMessage = "Please pick a date.")]
        [DataType(DataType.Date)]
        [Display(Name = "Booking Date")]
        public DateTime BookingDate { get; set; } = PhilippineTime.Today;

        [Required(ErrorMessage = "Please pick a start time.")]
        [Display(Name = "Start Time")]
        [DataType(DataType.Time)]
        public TimeSpan StartTime { get; set; }

        [Required]
        [Range(1, 8, ErrorMessage = "Duration must be between 1 and 8 hours.")]
        [Display(Name = "Duration (hours)")]
        public int DurationHours { get; set; } = 1;

        [Required(ErrorMessage = "Please enter your name.")]
        [StringLength(100)]
        [Display(Name = "Full Name")]
        public string CustomerName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your email.")]
        [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
        [StringLength(150)]
        [Display(Name = "Email Address")]
        public string CustomerEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your mobile number.")]
        [Phone(ErrorMessage = "Please enter a valid phone number.")]
        [StringLength(20)]
        [Display(Name = "Mobile Number")]
        public string CustomerMobile { get; set; } = string.Empty;

        [StringLength(500)]
        [Display(Name = "Notes (optional)")]
        public string? Notes { get; set; }

        [Required(ErrorMessage = "Please select a payment method.")]
        [Display(Name = "Payment Method")]
        public PaymentMethod? PaymentMethod { get; set; }

        [Display(Name = "Payment Amount")]
        public bool PayReservationFeeOnly { get; set; }

        [Display(Name = "Terms and Conditions")]
        public bool AcceptedTerms { get; set; }

        // For re-populating the dropdown on validation failures.
        public List<Court> Courts { get; set; } = new();
    }

    public class TimeSlotViewModel
    {
        public string StartTime { get; set; } = string.Empty;  // "HH:mm:ss" — used as form value
        public string EndTime { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;      // "8:00 AM – 9:00 AM" — display
        public bool IsAvailable { get; set; }
        public string? Reason { get; set; }                    // "Booked", "Blocked", "Past"
        public decimal HourlyRate { get; set; }
        public decimal TotalAmount { get; set; }
        public bool IsPromoRate { get; set; }
        public bool IsMixedRate { get; set; }
    }
}
