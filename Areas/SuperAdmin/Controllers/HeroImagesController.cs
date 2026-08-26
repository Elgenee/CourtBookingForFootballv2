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
    public class HeroImagesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<HeroImagesController> _logger;

        private static readonly string[] AllowedMime = { "image/png", "image/jpeg", "image/jpg", "image/webp" };
        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".webp" };
        private const long MaxBytes = 10 * 1024 * 1024;
        private const long UploadRequestBytes = 25 * 1024 * 1024;

        public HeroImagesController(ApplicationDbContext db, IWebHostEnvironment env, ILogger<HeroImagesController> logger)
        {
            _db = db; _env = env; _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var heroes = await _db.HeroImages.OrderByDescending(h => h.IsDefault).ThenByDescending(h => h.Id).ToListAsync();
            return View(heroes);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(UploadRequestBytes)]
        public async Task<IActionResult> Upload(HeroImageUploadViewModel vm)
        {
            if (vm.File == null || vm.File.Length == 0)
            {
                TempData["Error"] = "Please choose an image to upload.";
                return RedirectToAction(nameof(Index));
            }
            if (!IsAllowedImage(vm.File) || vm.File.Length > MaxBytes)
            {
                TempData["Error"] = "Hero image must be PNG/JPG/WebP and 10 MB or less.";
                return RedirectToAction(nameof(Index));
            }
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please provide a title.";
                return RedirectToAction(nameof(Index));
            }

            string? savedPath = null;
            try
            {
                savedPath = await SaveFileAsync(vm.File, "uploads/hero");
                _db.HeroImages.Add(new HeroImage
                {
                    Title = vm.Title.Trim(),
                    ImagePath = savedPath,
                    IsActive = vm.IsActive,
                    IsDefault = false,
                    CreatedDate = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(savedPath))
                {
                    DeleteIfLocal(savedPath);
                }

                _logger.LogError(ex, "Failed to upload hero image {FileName}", vm.File.FileName);
                TempData["Error"] = "The hero image could not be uploaded. Please try again with a different PNG, JPG, or WebP file.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = "Hero image uploaded.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDefault(int id)
        {
            var hero = await _db.HeroImages.FindAsync(id);
            if (hero == null) return NotFound();

            // Unset others, then set this one as default + active.
            await _db.HeroImages.Where(h => h.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(h => h.IsDefault, false));
            hero.IsDefault = true;
            hero.IsActive = true;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"\"{hero.Title}\" is now the active hero image.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var hero = await _db.HeroImages.FindAsync(id);
            if (hero == null) return NotFound();
            hero.IsActive = !hero.IsActive;
            if (!hero.IsActive) hero.IsDefault = false;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"\"{hero.Title}\" {(hero.IsActive ? "activated" : "deactivated")}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var hero = await _db.HeroImages.FindAsync(id);
            if (hero == null) return NotFound();
            DeleteIfLocal(hero.ImagePath);
            _db.HeroImages.Remove(hero);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Hero image deleted.";
            return RedirectToAction(nameof(Index));
        }

        // -------- helpers --------
        private static bool IsAllowedImage(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName);
            return AllowedMime.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase)
                || AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        private async Task<string> SaveFileAsync(IFormFile file, string subfolder)
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(webRoot, subfolder);
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
            {
                ext = file.ContentType.ToLowerInvariant() switch
                {
                    "image/webp" => ".webp",
                    "image/png" => ".png",
                    _ => ".jpg"
                };
            }
            var fileName = $"{Guid.NewGuid():N}{ext}";
            var full = Path.Combine(folder, fileName);
            using var stream = System.IO.File.Create(full);
            await file.CopyToAsync(stream);
            return $"{subfolder}/{fileName}";
        }

        private void DeleteIfLocal(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var full = Path.Combine(webRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(full))
            {
                try { System.IO.File.Delete(full); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete {Path}", full); }
            }
        }
    }
}
