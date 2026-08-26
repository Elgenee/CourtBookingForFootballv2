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

        public PromotionsController(ApplicationDbContext db)
        {
            _db = db;
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
        public async Task<IActionResult> Create(PromotionEditViewModel vm)
        {
            if (!IsValidDateRange(vm))
            {
                TempData["Error"] = "Promotion end date must be on or after the start date.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Please complete the required promotion fields.";
                return RedirectToAction(nameof(Index));
            }

            var maxOrder = await _db.Promotions.MaxAsync(p => (int?)p.DisplayOrder) ?? -1;
            _db.Promotions.Add(new Promotion
            {
                AnnouncementType = vm.AnnouncementType.Trim(),
                Title = vm.Title.Trim(),
                Description = vm.Description.Trim(),
                ImagePath = vm.ImagePath.Trim(),
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
                dbItem.AnnouncementType = input.AnnouncementType.Trim();
                dbItem.Title = input.Title.Trim();
                dbItem.Description = input.Description.Trim();
                dbItem.ImagePath = input.ImagePath.Trim();
                dbItem.ImageAlt = string.IsNullOrWhiteSpace(input.ImageAlt) ? null : input.ImageAlt.Trim();
                dbItem.ButtonLabel = input.ButtonLabel.Trim();
                dbItem.ButtonUrl = input.ButtonUrl.Trim();
                dbItem.IconHtml = input.IconHtml.Trim();
                dbItem.StartDate = input.StartDate?.Date;
                dbItem.EndDate = input.EndDate?.Date;
                dbItem.DisplayOrder = input.DisplayOrder;
                dbItem.IsActive = input.IsActive;
                dbItem.UpdatedDate = DateTime.UtcNow;
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
    }
}
