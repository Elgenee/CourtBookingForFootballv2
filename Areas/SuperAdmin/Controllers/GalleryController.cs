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
    public class GalleryController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<GalleryController> _logger;

        private static readonly string[] AllowedMime = { "image/png", "image/jpeg", "image/jpg", "image/webp" };
        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".webp" };
        private const long MaxBytes = 10 * 1024 * 1024;
        private const long UploadRequestBytes = 25 * 1024 * 1024;

        public GalleryController(ApplicationDbContext db, IWebHostEnvironment env, ILogger<GalleryController> logger)
        {
            _db = db; _env = env; _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var items = await _db.GalleryImages.OrderBy(g => g.DisplayOrder).ThenBy(g => g.Id).ToListAsync();
            return View(items);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(UploadRequestBytes)]
        public async Task<IActionResult> Upload(GalleryImageUploadViewModel vm)
        {
            if (vm.File == null || vm.File.Length == 0)
            {
                TempData["Error"] = "Please choose an image file.";
                return RedirectToAction(nameof(Index));
            }
            if (!IsAllowedImage(vm.File) || vm.File.Length > MaxBytes)
            {
                TempData["Error"] = "Gallery image must be PNG/JPG/WebP and 10 MB or less.";
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
                savedPath = await SaveFileAsync(vm.File, "uploads/gallery");
                var maxOrder = await _db.GalleryImages.MaxAsync(g => (int?)g.DisplayOrder) ?? -1;

                _db.GalleryImages.Add(new GalleryImage
                {
                    Title = vm.Title.Trim(),
                    Caption = string.IsNullOrWhiteSpace(vm.Caption) ? null : vm.Caption.Trim(),
                    ImagePath = savedPath,
                    DisplayOrder = maxOrder + 1,
                    IsActive = vm.IsActive,
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

                _logger.LogError(ex, "Failed to upload gallery image {FileName}", vm.File.FileName);
                TempData["Error"] = "The gallery image could not be uploaded. Please try again with a different PNG, JPG, or WebP file up to 10 MB.";
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = "Gallery image uploaded.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(List<GalleryImageEditViewModel> items)
        {
            if (items == null || items.Count == 0)
            {
                return RedirectToAction(nameof(Index));
            }

            var ids = items.Select(i => i.Id).ToList();
            var dbItems = await _db.GalleryImages.Where(g => ids.Contains(g.Id)).ToListAsync();

            foreach (var dbItem in dbItems)
            {
                var input = items.First(i => i.Id == dbItem.Id);
                dbItem.Title = (input.Title ?? string.Empty).Trim();
                dbItem.Caption = string.IsNullOrWhiteSpace(input.Caption) ? null : input.Caption.Trim();
                dbItem.DisplayOrder = input.DisplayOrder;
                dbItem.IsActive = input.IsActive;
            }
            await _db.SaveChangesAsync();
            TempData["Success"] = "Gallery updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Move(int id, string direction)
        {
            var current = await _db.GalleryImages.FindAsync(id);
            if (current == null) return NotFound();

            var ordered = await _db.GalleryImages.OrderBy(g => g.DisplayOrder).ThenBy(g => g.Id).ToListAsync();
            var idx = ordered.FindIndex(g => g.Id == id);
            var swapIdx = direction == "up" ? idx - 1 : idx + 1;
            if (swapIdx >= 0 && swapIdx < ordered.Count)
            {
                var other = ordered[swapIdx];
                (current.DisplayOrder, other.DisplayOrder) = (other.DisplayOrder, current.DisplayOrder);
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _db.GalleryImages.FindAsync(id);
            if (item == null) return NotFound();
            DeleteIfLocal(item.ImagePath);
            _db.GalleryImages.Remove(item);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Gallery image deleted.";
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
