using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.Models.Cms
{
    public class HeroImage
    {
        public int Id { get; set; }

        [Required, StringLength(260)]
        public string ImagePath { get; set; } = string.Empty;

        [Required, StringLength(140)]
        public string Title { get; set; } = string.Empty;

        // Only one row should have IsDefault = true at a time (enforced in controller).
        public bool IsDefault { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
