using CourtBookingSystem.Areas.Admin.ViewModels;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class BlockedSlotsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public BlockedSlotsController(ApplicationDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            var vm = new BlockedSlotFormViewModel
            {
                Courts = await _db.Courts.OrderBy(c => c.CourtName).ToListAsync(),
                Existing = (await _db.BlockedSlots
                    .Include(s => s.Court)
                    .OrderByDescending(s => s.BlockedDate)
                    .Take(100)
                    .ToListAsync())
                    .OrderByDescending(s => s.BlockedDate)
                    .ThenBy(s => s.StartTime)
                    .ToList()
            };
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BlockedSlotFormViewModel vm)
        {
            // Repopulate lookup data in case of validation failure
            vm.Courts = await _db.Courts.OrderBy(c => c.CourtName).ToListAsync();
            vm.Existing = (await _db.BlockedSlots
                .Include(s => s.Court)
                .OrderByDescending(s => s.BlockedDate)
                .Take(100)
                .ToListAsync())
                .OrderByDescending(s => s.BlockedDate)
                .ThenBy(s => s.StartTime)
                .ToList();

            if (vm.EndTime <= vm.StartTime)
            {
                ModelState.AddModelError(nameof(vm.EndTime), "End time must be after start time.");
            }
            if (vm.BlockedDate.Date < PhilippineTime.Today)
            {
                ModelState.AddModelError(nameof(vm.BlockedDate), "Blocked date cannot be in the past.");
            }
            if (!ModelState.IsValid) return View(nameof(Index), vm);

            _db.BlockedSlots.Add(new BlockedSlot
            {
                CourtId = vm.CourtId,
                BlockedDate = vm.BlockedDate.Date,
                StartTime = vm.StartTime,
                EndTime = vm.EndTime,
                Reason = string.IsNullOrWhiteSpace(vm.Reason) ? null : vm.Reason.Trim(),
                CreatedDate = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = "Slot blocked successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var slot = await _db.BlockedSlots.FindAsync(id);
            if (slot == null) return NotFound();
            _db.BlockedSlots.Remove(slot);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Blocked slot removed.";
            return RedirectToAction(nameof(Index));
        }
    }
}
