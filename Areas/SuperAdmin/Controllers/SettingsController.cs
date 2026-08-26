using CourtBookingSystem.Areas.SuperAdmin.ViewModels;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models.Cms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.SuperAdmin.Controllers
{
    [Area("SuperAdmin")]
    [Authorize(Roles = "SuperAdmin")]
    public class SettingsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<SettingsController> _logger;

        // Whitelist for navbar logo. SVG allowed per spec.
        private static readonly string[] AllowedLogoMime = { "image/png", "image/jpeg", "image/svg+xml" };
        private static readonly string[] AllowedLogoExt = { ".png", ".jpg", ".jpeg", ".svg" };
        private const long MaxLogoBytes = 2 * 1024 * 1024;

        public SettingsController(ApplicationDbContext db, IWebHostEnvironment env, ILogger<SettingsController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        // Default action for the area: redirect to Website settings.
        public IActionResult Index() => RedirectToAction(nameof(Website));

        [HttpGet]
        public async Task<IActionResult> Website()
        {
            var setting = await GetEditableWebsiteSettingQuery().FirstOrDefaultAsync()
                ?? new Models.Cms.WebsiteSetting();

            var vm = new WebsiteSettingsViewModel
            {
                WebsiteName = setting.WebsiteName,
                WebsiteTagline = setting.WebsiteTagline,
                ExistingLogoPath = setting.NavbarLogoPath,
                LandingColorScheme = LandingColorSchemeCatalog.GetByKey(setting.LandingColorScheme).Key,
                ColorSchemes = LandingColorSchemeCatalog.All,
                HeroTitle = setting.HeroTitle,
                HeroSubtitle = setting.HeroSubtitle,
                HeroButtonText = setting.HeroButtonText
            };
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxLogoBytes + 1024)]
        public async Task<IActionResult> Website(WebsiteSettingsViewModel vm)
        {
            var setting = await GetEditableWebsiteSettingQuery().FirstOrDefaultAsync();
            var isNew = setting == null;
            setting ??= new Models.Cms.WebsiteSetting();

            if (vm.LogoFile != null && vm.LogoFile.Length > 0)
            {
                var saved = await SaveLogoAsync(vm.LogoFile);
                if (saved == null)
                {
                    ModelState.AddModelError(nameof(vm.LogoFile), "Logo must be PNG, JPG, or SVG (max 2 MB).");
                }
                else
                {
                    DeleteIfLocal(setting.NavbarLogoPath);
                    setting.NavbarLogoPath = saved;
                }
            }
            else if (vm.RemoveLogo && !string.IsNullOrEmpty(setting.NavbarLogoPath))
            {
                DeleteIfLocal(setting.NavbarLogoPath);
                setting.NavbarLogoPath = null;
            }

            if (!LandingColorSchemeCatalog.IsValid(vm.LandingColorScheme))
            {
                ModelState.AddModelError(nameof(vm.LandingColorScheme), "Choose one of the available color schemes.");
            }

            if (!ModelState.IsValid)
            {
                vm.ExistingLogoPath = setting.NavbarLogoPath;
                vm.ColorSchemes = LandingColorSchemeCatalog.All;
                return View(vm);
            }

            setting.WebsiteName = vm.WebsiteName.Trim();
            setting.WebsiteTagline = string.IsNullOrWhiteSpace(vm.WebsiteTagline) ? null : vm.WebsiteTagline.Trim();
            setting.LandingColorScheme = LandingColorSchemeCatalog.GetByKey(vm.LandingColorScheme).Key;
            setting.HeroTitle = vm.HeroTitle.Trim();
            setting.HeroSubtitle = string.IsNullOrWhiteSpace(vm.HeroSubtitle) ? null : vm.HeroSubtitle.Trim();
            setting.HeroButtonText = vm.HeroButtonText.Trim();
            setting.UpdatedDate = DateTime.UtcNow;

            if (isNew) _db.WebsiteSettings.Add(setting);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Website settings updated.";
            return RedirectToAction(nameof(Website));
        }

        // ---------- helpers ----------

        private IQueryable<WebsiteSetting> GetEditableWebsiteSettingQuery()
        {
            return _db.WebsiteSettings
                .OrderByDescending(s => s.UpdatedDate)
                .ThenByDescending(s => s.Id);
        }

        private async Task<string?> SaveLogoAsync(IFormFile file)
        {
            if (file.Length > MaxLogoBytes) return null;
            if (!AllowedLogoMime.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase)) return null;

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedLogoExt.Contains(ext))
            {
                ext = file.ContentType.ToLowerInvariant() switch
                {
                    "image/svg+xml" => ".svg",
                    "image/jpeg" => ".jpg",
                    _ => ".png"
                };
            }

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(webRoot, "uploads", "branding");
            Directory.CreateDirectory(folder);

            var fileName = $"logo-{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(folder, fileName);
            using (var stream = System.IO.File.Create(fullPath))
            {
                await file.CopyToAsync(stream);
            }
            return $"uploads/branding/{fileName}";
        }

        private void DeleteIfLocal(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return;
            if (relativePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var full = Path.Combine(webRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(full))
            {
                try { System.IO.File.Delete(full); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete asset {Path}", full); }
            }
        }
    }
}
