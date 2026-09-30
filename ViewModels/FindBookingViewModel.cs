using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.ViewModels
{
    public class FindBookingViewModel
    {
        [Required(ErrorMessage = "Please enter your booking reference.")]
        [Display(Name = "Booking Reference")]
        public string BookingReferenceNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the mobile number used for the booking.")]
        [Display(Name = "Mobile Number")]
        public string CustomerMobile { get; set; } = string.Empty;
    }
}
