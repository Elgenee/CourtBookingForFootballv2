using CourtBookingSystem.Data;
using CourtBookingSystem.Models.Cms;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services
{
    /// <summary>
    /// Single read accessor for CMS content. Scoped (one cache per request) so
    /// the layout can fetch WebsiteSettings/About once even if multiple partials need it.
    /// </summary>
    public class SiteContentService
    {
        private readonly ApplicationDbContext _db;
        private WebsiteSetting? _settings;
        private AboutContent? _about;
        private HeroImage? _hero;
        private bool _heroLoaded;

        public SiteContentService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<WebsiteSetting> GetSettingsAsync()
        {
            if (_settings != null) return _settings;
            _settings = await _db.WebsiteSettings.AsNoTracking().FirstOrDefaultAsync()
                ?? new WebsiteSetting();
            return _settings;
        }

        public async Task<AboutContent> GetAboutAsync()
        {
            if (_about != null) return _about;
            _about = await _db.AboutContents.AsNoTracking().FirstOrDefaultAsync()
                ?? new AboutContent();
            return _about;
        }

        public async Task<HeroImage?> GetActiveHeroAsync()
        {
            if (_heroLoaded) return _hero;
            _heroLoaded = true;
            _hero = await _db.HeroImages
                .AsNoTracking()
                .Where(h => h.IsActive)
                .OrderByDescending(h => h.IsDefault)
                .ThenBy(h => h.Id)
                .FirstOrDefaultAsync();
            return _hero;
        }

        public async Task<List<GalleryImage>> GetGalleryAsync()
        {
            return await _db.GalleryImages
                .AsNoTracking()
                .Where(g => g.IsActive)
                .OrderBy(g => g.DisplayOrder)
                .ThenBy(g => g.Id)
                .ToListAsync();
        }

        public async Task<List<Promotion>> GetActivePromotionsAsync(DateTime today)
        {
            var date = today.Date;
            return await _db.Promotions
                .AsNoTracking()
                .Where(p => p.IsActive
                    && (!p.StartDate.HasValue || p.StartDate.Value.Date <= date)
                    && (!p.EndDate.HasValue || p.EndDate.Value.Date >= date))
                .OrderBy(p => p.DisplayOrder)
                .ThenBy(p => p.Id)
                .Take(3)
                .ToListAsync();
        }
    }
}
