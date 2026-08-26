using CourtBookingSystem.Areas.Admin.ViewModels;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class CourtsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public CourtsController(ApplicationDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            var courts = await _db.Courts
                .Include(c => c.CourtGroup)
                .OrderBy(c => c.SportType)
                .ThenBy(c => c.CourtName)
                .ToListAsync();
            return View(courts);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var vm = new CourtFormViewModel
            {
                AvailableGroups = await GetActiveGroupsAsync()
            };
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CourtFormViewModel vm)
        {
            vm.AvailableGroups = await GetActiveGroupsAsync();
            if (!ModelState.IsValid) return View(vm);

            _db.Courts.Add(new Court
            {
                CourtName = vm.CourtName.Trim(),
                SportType = vm.SportType,
                Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim(),
                HourlyRate = 0m,
                IsPromoRateEnabled = false,
                PromoHourlyRate = null,
                PromoStartTime = null,
                PromoEndTime = null,
                PromoDayMask = 0,
                Status = vm.Status,
                IsActive = vm.IsActive,
                CourtGroupId = vm.CourtGroupId,
                IsFullCourt = vm.IsFullCourt
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Court '{vm.CourtName}' created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var court = await _db.Courts.FindAsync(id);
            if (court == null) return NotFound();
            var vm = CourtFormViewModel.From(court);
            vm.AvailableGroups = await GetActiveGroupsAsync();
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CourtFormViewModel vm)
        {
            vm.AvailableGroups = await GetActiveGroupsAsync();
            if (!ModelState.IsValid) return View(vm);

            var court = await _db.Courts.FindAsync(vm.Id);
            if (court == null) return NotFound();

            court.CourtName = vm.CourtName.Trim();
            court.SportType = vm.SportType;
            court.Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim();
            court.HourlyRate = 0m;
            court.IsPromoRateEnabled = false;
            court.PromoHourlyRate = null;
            court.PromoStartTime = null;
            court.PromoEndTime = null;
            court.PromoDayMask = 0;
            court.Status = vm.Status;
            court.IsActive = vm.IsActive;
            court.CourtGroupId = vm.CourtGroupId;
            court.IsFullCourt = vm.IsFullCourt;

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Court '{vm.CourtName}' updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var court = await _db.Courts.FindAsync(id);
            if (court == null) return NotFound();

            // Hard-delete only if no bookings reference it; otherwise soft-delete.
            var hasBookings = await _db.Bookings.AnyAsync(b => b.CourtId == id);
            if (hasBookings)
            {
                court.IsActive = false;
                TempData["Success"] = $"Court '{court.CourtName}' deactivated (has existing bookings).";
            }
            else
            {
                _db.Courts.Remove(court);
                TempData["Success"] = $"Court '{court.CourtName}' deleted.";
            }
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private async Task<List<CourtGroup>> GetActiveGroupsAsync() =>
            await _db.CourtGroups
                .Where(g => g.IsActive)
                .OrderBy(g => g.GroupName)
                .ToListAsync();
    }
}
