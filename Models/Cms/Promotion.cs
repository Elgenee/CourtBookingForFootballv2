using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.Models.Cms
{
    public class Promotion
    {
        public int Id { get; set; }

        [Required, StringLength(80)]
        public string AnnouncementType { get; set; } = "Promo";

        [Required, StringLength(140)]
        public string Title { get; set; } = string.Empty;

        [Required, StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required, StringLength(260)]
        public string ImagePath { get; set; } = string.Empty;

        [StringLength(180)]
        public string? ImageAlt { get; set; }

        [Required, StringLength(80)]
        public string ButtonLabel { get; set; } = "Book Now";

        [Required, StringLength(260)]
        public string ButtonUrl { get; set; } = "#booking-search";

        [Required, StringLength(40)]
        public string IconHtml { get; set; } = "&#x1F389;";

        public DateTime? StartDate { get; set; }

        public DateTime? EndDate { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
    }
}
