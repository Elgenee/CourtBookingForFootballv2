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
    public class PromotionsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PromotionsController> _logger;

        private static readonly string[] AllowedImageMime =
        {
            "image/png", "image/jpeg", "image/jpg", "image/webp", "image/svg+xml"
        };
        private static readonly string[] AllowedImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".webp", ".svg"
        };
        private const long MaxImageBytes = 5 * 1024 * 1024;
        private const long UploadRequestBytes = 25 * 1024 * 1024;

        public PromotionsController(ApplicationDbContext db, IWebHostEnvironment env, ILogger<PromotionsController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var items = await _db.Promotions
                .OrderBy(p => p.DisplayOrder)
                .ThenBy(p => p.Id)
                .ToListAsync();

            return View(items);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(UploadRequestBytes)]
        public async Task<IActionResult> Create(PromotionEditViewModel vm)
        {
            if (!IsValidDateRange(vm))
            {
                TempData["Error"] = "Promotion end date must be on or after the start date.";
                return RedirectToAction(nameof(Index));
            }

            if (vm.ImageFile == null && string.IsNullOrWhiteSpace(vm.ImagePath))
            {
                TempData["Error"] = "Please upload an image or enter an image path.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please complete the required promotion fields.";
                return RedirectToAction(nameof(Index));
            }

            var imagePath = string.IsNullOrWhiteSpace(vm.ImagePath) ? null : vm.ImagePath.Trim();
            if (vm.ImageFile != null && vm.ImageFile.Length > 0)
            {
                if (!IsAllowedImage(vm.ImageFile) || vm.ImageFile.Length > MaxImageBytes)
                {
                    TempData["Error"] = "Promotion image must be PNG, JPG, WebP, or SVG and 5 MB or less.";
                    return RedirectToAction(nameof(Index));
                }

                imagePath = await SaveImageAsync(vm.ImageFile);
            }

            var maxOrder = await _db.Promotions.MaxAsync(p => (int?)p.DisplayOrder) ?? -1;
            _db.Promotions.Add(new Promotion
            {
                AnnouncementType = vm.AnnouncementType.Trim(),
                Title = vm.Title.Trim(),
                Description = vm.Description.Trim(),
                ImagePath = imagePath!,
                ImageAlt = string.IsNullOrWhiteSpace(vm.ImageAlt) ? null : vm.ImageAlt.Trim(),
                ButtonLabel = vm.ButtonLabel.Trim(),
                ButtonUrl = vm.ButtonUrl.Trim(),
                IconHtml = vm.IconHtml.Trim(),
                StartDate = vm.StartDate?.Date,
                EndDate = vm.EndDate?.Date,
                DisplayOrder = maxOrder + 1,
                IsActive = vm.IsActive,
                UpdatedDate = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = "Promotion created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        [RequestSizeLimit(UploadRequestBytes)]
        public async Task<IActionResult> Update(List<PromotionEditViewModel> items)
        {
            if (items == null || items.Count == 0)
            {
                return RedirectToAction(nameof(Index));
            }

            foreach (var item in items)
            {
                if (!IsValidDateRange(item))
                {
                    TempData["Error"] = $"Promotion '{item.Title}' has an end date before the start date.";
                    return RedirectToAction(nameof(Index));
                }

                if (item.ImageFile == null && string.IsNullOrWhiteSpace(item.ImagePath))
                {
                    TempData["Error"] = $"Promotion '{item.Title}' needs an uploaded image or image path.";
                    return RedirectToAction(nameof(Index));
                }

                if (item.ImageFile != null
                    && item.ImageFile.Length > 0
                    && (!IsAllowedImage(item.ImageFile) || item.ImageFile.Length > MaxImageBytes))
                {
                    TempData["Error"] = $"Promotion '{item.Title}' image must be PNG, JPG, WebP, or SVG and 5 MB or less.";
                    return RedirectToAction(nameof(Index));
                }
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please complete the required promotion fields.";
                return RedirectToAction(nameof(Index));
            }

            var ids = items.Select(i => i.Id).ToList();
            var dbItems = await _db.Promotions.Where(p => ids.Contains(p.Id)).ToListAsync();

            foreach (var dbItem in dbItems)
            {
                var input = items.First(i => i.Id == dbItem.Id);
                var previousImagePath = dbItem.ImagePath;
                dbItem.AnnouncementType = input.AnnouncementType.Trim();
                dbItem.Title = input.Title.Trim();
                dbItem.Description = input.Description.Trim();
                dbItem.ImagePath = input.ImageFile != null && input.ImageFile.Length > 0
                    ? await SaveImageAsync(input.ImageFile)
                    : input.ImagePath!.Trim();
                dbItem.ImageAlt = string.IsNullOrWhiteSpace(input.ImageAlt) ? null : input.ImageAlt.Trim();
                dbItem.ButtonLabel = input.ButtonLabel.Trim();
                dbItem.ButtonUrl = input.ButtonUrl.Trim();
                dbItem.IconHtml = input.IconHtml.Trim();
                dbItem.StartDate = input.StartDate?.Date;
                dbItem.EndDate = input.EndDate?.Date;
                dbItem.DisplayOrder = input.DisplayOrder;
                dbItem.IsActive = input.IsActive;
                dbItem.UpdatedDate = DateTime.UtcNow;

                if (!string.Equals(previousImagePath, dbItem.ImagePath, StringComparison.OrdinalIgnoreCase))
                {
                    DeleteIfLocal(previousImagePath);
                }
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "Promotions updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _db.Promotions.FindAsync(id);
            if (item == null) return NotFound();

            DeleteIfLocal(item.ImagePath);
            _db.Promotions.Remove(item);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Promotion deleted.";
            return RedirectToAction(nameof(Index));
        }

        private static bool IsValidDateRange(PromotionEditViewModel vm)
        {
            return !vm.StartDate.HasValue
                || !vm.EndDate.HasValue
                || vm.EndDate.Value.Date >= vm.StartDate.Value.Date;
        }

        private static bool IsAllowedImage(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName);
            return AllowedImageMime.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase)
                || AllowedImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        private async Task<string> SaveImageAsync(IFormFile file)
        {
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(webRoot, "uploads", "promotions");
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(ext) || !AllowedImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                ext = file.ContentType.ToLowerInvariant() switch
                {
                    "image/svg+xml" => ".svg",
                    "image/webp" => ".webp",
                    "image/png" => ".png",
                    _ => ".jpg"
                };
            }

            var fileName = $"promotion-{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(folder, fileName);
            using var stream = System.IO.File.Create(fullPath);
            await file.CopyToAsync(stream);
            return $"uploads/promotions/{fileName}";
        }

        private void DeleteIfLocal(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

            var normalized = path.TrimStart('/').Replace('\\', '/');
            if (!normalized.StartsWith("uploads/promotions/", StringComparison.OrdinalIgnoreCase)) return;

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var fullPath = Path.Combine(webRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(fullPath)) return;

            try { System.IO.File.Delete(fullPath); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete promotion image {Path}", fullPath); }
        }
    }
}
