using System.ComponentModel.DataAnnotations;

namespace CourtBookingSystem.Models.Cms
{
    /// <summary>
    /// Singleton row (Id = 1) that drives navbar brand + global site copy.
    /// Managed by SuperAdmin via /SuperAdmin/Settings/Website.
    /// </summary>
    public class WebsiteSetting
    {
        public int Id { get; set; }

        [Required, StringLength(120)]
        public string WebsiteName { get; set; } = "Royal Court";

        [StringLength(240)]
        public string? WebsiteTagline { get; set; }

        // Path relative to wwwroot, e.g. "uploads/branding/logo.png". Null = use fallback icon.
        [StringLength(260)]
        public string? NavbarLogoPath { get; set; }

        [Required, StringLength(40)]
        public string LandingColorScheme { get; set; } = LandingColorSchemeCatalog.DefaultKey;

        // Hero block (text fields — the picture lives on HeroImage)
        [Required, StringLength(160)]
        public string HeroTitle { get; set; } = "Book Your Next Game";

        [StringLength(400)]
        public string? HeroSubtitle { get; set; }

        [Required, StringLength(80)]
        public string HeroButtonText { get; set; } = "Find Available Times";

        public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
    }
}
