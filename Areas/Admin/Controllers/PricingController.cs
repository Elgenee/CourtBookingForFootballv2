using CourtBookingSystem.Areas.Admin.ViewModels;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class PricingController : Controller
    {
        private readonly ApplicationDbContext _db;

        public PricingController(ApplicationDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            return View(new BookingPricingIndexViewModel
            {
                Rules = await GetRulesAsync(),
                Courts = await GetCourtsAsync()
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BookingPricingIndexViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                vm.Rules = await GetRulesAsync();
                vm.Courts = await GetCourtsAsync();
                vm.NewRule.Courts = vm.Courts;
                return View(nameof(Index), vm);
            }

            if (!await CourtExistsAsync(vm.NewRule.CourtId))
            {
                ModelState.AddModelError("NewRule.CourtId", "Please choose an active field.");
                vm.Rules = await GetRulesAsync();
                vm.Courts = await GetCourtsAsync();
                vm.NewRule.Courts = vm.Courts;
                return View(nameof(Index), vm);
            }

            _db.BookingPricingRules.Add(ToRule(vm.NewRule));
            await _db.SaveChangesAsync();

            TempData["Success"] = "Pricing rule created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var rule = await _db.BookingPricingRules.FindAsync(id);
            if (rule == null) return NotFound();

            var vm = BookingPricingRuleFormViewModel.From(rule);
            vm.Courts = await GetCourtsAsync();
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(BookingPricingRuleFormViewModel vm)
        {
            vm.Courts = await GetCourtsAsync();
            if (!ModelState.IsValid) return View(vm);

            if (!await CourtExistsAsync(vm.CourtId))
            {
                ModelState.AddModelError(nameof(vm.CourtId), "Please choose an active field.");
                return View(vm);
            }

            var rule = await _db.BookingPricingRules.FindAsync(vm.Id);
            if (rule == null) return NotFound();

            Apply(rule, vm);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Pricing rule updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id)
        {
            var rule = await _db.BookingPricingRules.FindAsync(id);
            if (rule == null) return NotFound();

            rule.IsEnabled = !rule.IsEnabled;
            await _db.SaveChangesAsync();

            TempData["Success"] = rule.IsEnabled ? "Pricing rule enabled." : "Pricing rule disabled.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var rule = await _db.BookingPricingRules.FindAsync(id);
            if (rule == null) return NotFound();

            _db.BookingPricingRules.Remove(rule);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Pricing rule deleted.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<List<BookingPricingRule>> GetRulesAsync() =>
            await _db.BookingPricingRules
                .AsNoTracking()
                .Include(r => r.Court)
                .OrderBy(r => r.Court!.CourtName)
                .ThenBy(r => r.IsPromotional)
                .ThenBy(r => r.DisplayOrder)
                .ThenBy(r => r.StartTime)
                .ToListAsync();

        private async Task<List<Court>> GetCourtsAsync() =>
            await _db.Courts
                .Where(c => c.IsActive
                    && c.Status != CourtStatus.Closed
                    && c.Status != CourtStatus.UnderMaintenance)
                .OrderBy(c => c.CourtName)
                .ToListAsync();

        private Task<bool> CourtExistsAsync(int courtId) =>
            _db.Courts.AnyAsync(c => c.Id == courtId && c.IsActive);

        private static BookingPricingRule ToRule(BookingPricingRuleFormViewModel vm)
        {
            var rule = new BookingPricingRule();
            Apply(rule, vm);
            return rule;
        }

        private static void Apply(BookingPricingRule rule, BookingPricingRuleFormViewModel vm)
        {
            rule.CourtId = vm.CourtId;
            rule.Name = vm.Name.Trim();
            rule.IsPromotional = vm.IsPromotional;
            rule.IsEnabled = vm.IsEnabled;
            rule.EffectiveStartDate = vm.IsPromotional ? vm.EffectiveStartDate?.Date : null;
            rule.EffectiveEndDate = vm.IsPromotional ? vm.EffectiveEndDate?.Date : null;
            rule.StartTime = vm.StartTime;
            rule.EndTime = vm.EndTime;
            rule.HourlyRate = vm.HourlyRate;
            rule.Priority = vm.IsPromotional ? vm.Priority : 0;
            rule.DisplayOrder = vm.DisplayOrder;
        }
    }
}
