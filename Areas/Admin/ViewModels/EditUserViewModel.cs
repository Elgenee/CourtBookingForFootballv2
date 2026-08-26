using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.Areas.Admin.ViewModels
{
    public class EditUserViewModel
    {

        [Required, StringLength(150)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        [Required]
        public string Id { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Username")]
        public string UserName { get; set; } = string.Empty;

        [Phone]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        [Display(Name = "Email confirmed")]
        public bool EmailConfirmed { get; set; }

        [Display(Name = "Lockout enabled")]
        public bool LockoutEnabled { get; set; }
    }
}