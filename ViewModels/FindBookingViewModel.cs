using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.ViewModels
{
    public class FindBookingViewModel
    {
        [Required(ErrorMessage = "Please enter your booking reference.")]
        [RegularExpression(@"[Gg][Ff][Cc]-[0-9]{8}-[A-Za-z0-9]{6}",
            ErrorMessage = "Use GFC-YYYYMMDD-XXXXXX: 8 date digits followed by your 6-character booking code.")]
        [Display(Name = "Booking Reference")]
        public string BookingReferenceNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the mobile number used for the booking.")]
        [Display(Name = "Mobile Number")]
        public string CustomerMobile { get; set; } = string.Empty;
    }
}
