using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.Models.Cms
{
    public class GalleryImage
    {
        public int Id { get; set; }

        [Required, StringLength(260)]
        public string ImagePath { get; set; } = string.Empty;

        [Required, StringLength(140)]
        public string Title { get; set; } = string.Empty;

        [StringLength(400)]
        public string? Caption { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
