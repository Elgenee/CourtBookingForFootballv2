using System.ComponentModel.DataAnnotations;
using CourtBookingSystem.Models.Cms;
using Microsoft.AspNetCore.Http;

namespace CourtBookingSystem.Areas.SuperAdmin.ViewModels
{
    public class WebsiteSettingsViewModel
    {
        [Required, StringLength(120)]
        [Display(Name = "Website Name")]
        public string WebsiteName { get; set; } = "Royal Court";

        [StringLength(240)]
        [Display(Name = "Website Tagline")]
        public string? WebsiteTagline { get; set; }

        [Display(Name = "Navbar Logo (PNG / JPG / SVG)")]
        public IFormFile? LogoFile { get; set; }

        public string? ExistingLogoPath { get; set; }

        public bool RemoveLogo { get; set; }

        [Required, StringLength(40)]
        [Display(Name = "Landing Page Color Scheme")]
        public string LandingColorScheme { get; set; } = LandingColorSchemeCatalog.DefaultKey;

        public IReadOnlyList<LandingColorScheme> ColorSchemes { get; set; } =
            LandingColorSchemeCatalog.All;

        [Required, StringLength(160)]
        [Display(Name = "Hero Title")]
        public string HeroTitle { get; set; } = string.Empty;

        [StringLength(400)]
        [Display(Name = "Hero Subtitle")]
        public string? HeroSubtitle { get; set; }

        [Required, StringLength(80)]
        [Display(Name = "Hero Button Text")]
        public string HeroButtonText { get; set; } = "Find Available Times";
    }

    public class HeroImageUploadViewModel
    {
        [Required, StringLength(140)]
        [Display(Name = "Title")]
        public string Title { get; set; } = string.Empty;

        [Display(Name = "Image File (PNG / JPG / WebP)")]
        public IFormFile? File { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class GalleryImageUploadViewModel
    {
        [Required, StringLength(140)]
        [Display(Name = "Title")]
        public string Title { get; set; } = string.Empty;

        [StringLength(400)]
        [Display(Name = "Caption")]
        public string? Caption { get; set; }

        [Display(Name = "Image File (PNG / JPG / WebP)")]
        public IFormFile? File { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class GalleryImageEditViewModel
    {
        public int Id { get; set; }

        [Required, StringLength(140)]
        public string Title { get; set; } = string.Empty;

        [StringLength(400)]
        public string? Caption { get; set; }

        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PromotionEditViewModel
    {
        public int Id { get; set; }

        [Required, StringLength(80)]
        [Display(Name = "Type")]
        public string AnnouncementType { get; set; } = "Promo";

        [Required, StringLength(140)]
        public string Title { get; set; } = string.Empty;

        [Required, StringLength(500)]
        public string Description { get; set; } = string.Empty;

        [Required, StringLength(260)]
        [Display(Name = "Image Path")]
        public string ImagePath { get; set; } = string.Empty;

        [StringLength(180)]
        [Display(Name = "Image Alt Text")]
        public string? ImageAlt { get; set; }

        [Required, StringLength(80)]
        [Display(Name = "Button Label")]
        public string ButtonLabel { get; set; } = "Book Now";

        [Required, StringLength(260)]
        [Display(Name = "Button URL")]
        public string ButtonUrl { get; set; } = "#booking-search";

        [Required, StringLength(40)]
        [Display(Name = "Icon HTML")]
        public string IconHtml { get; set; } = "&#x1F389;";

        [DataType(DataType.Date)]
        [Display(Name = "Start Date")]
        public DateTime? StartDate { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "End Date")]
        public DateTime? EndDate { get; set; }

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public class AboutContentViewModel
    {
        [Required, StringLength(140)]
        [Display(Name = "About Title")]
        public string Title { get; set; } = string.Empty;

        [Required, StringLength(2000)]
        [Display(Name = "About Description")]
        public string Description { get; set; } = string.Empty;

        [Display(Name = "Facilities (one per line)")]
        public string FacilitiesList { get; set; } = string.Empty;

        [Display(Name = "Contact Phone")]
        [StringLength(120)]
        public string? ContactPhone { get; set; }

        [Display(Name = "Contact Email")]
        [StringLength(160)]
        public string? ContactEmail { get; set; }

        [Display(Name = "Location")]
        [StringLength(240)]
        public string? Location { get; set; }

        [Display(Name = "Opening Hours — Days")]
        [StringLength(80)]
        public string? OpeningHoursDays { get; set; }

        [Display(Name = "Opening Hours — Times")]
        [StringLength(80)]
        public string? OpeningHoursTimes { get; set; }
    }
}
