using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.Models.Cms
{
    /// <summary>
    /// Singleton row (Id = 1) for the public "About" section.
    /// Facilities are stored as a newline-separated list in <see cref="FacilitiesList"/>.
    /// </summary>
    public class AboutContent
    {
        public int Id { get; set; }

        [Required, StringLength(140)]
        public string Title { get; set; } = "About Giuseppe Football";

        [Required, StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// One facility per line. Rendered as a bulleted list on the public site.
        /// </summary>
        [StringLength(2000)]
        public string FacilitiesList { get; set; } = string.Empty;

        // Contact info
        [StringLength(120)]
        public string? ContactPhone { get; set; }

        [StringLength(160)]
        public string? ContactEmail { get; set; }

        [StringLength(240)]
        public string? Location { get; set; }

        // Opening hours — two lines so we can render e.g. "Mon – Sun" + "8:00 AM – 10:00 PM"
        [StringLength(80)]
        public string? OpeningHoursDays { get; set; }

        [StringLength(80)]
        public string? OpeningHoursTimes { get; set; }

        public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
    }
}
