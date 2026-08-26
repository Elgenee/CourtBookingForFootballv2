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
    public class CourtGroupsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public CourtGroupsController(ApplicationDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            var groups = await _db.CourtGroups
                .Include(g => g.Courts.OrderBy(c => c.CourtName))
                .OrderByDescending(g => g.IsActive)
                .ThenBy(g => g.GroupName)
                .ToListAsync();
            return View(groups);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var vm = new CourtGroupFormViewModel
            {
                IsActive = true,
                AvailableCourts = await GetAssignableCourtsAsync(currentGroupId: null)
            };
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CourtGroupFormViewModel vm)
        {
            await NormalizeCodeAsync(vm);
            vm.AvailableCourts = await GetAssignableCourtsAsync(currentGroupId: null);

            if (await _db.CourtGroups.AnyAsync(g => g.GroupCode == vm.GroupCode))
            {
                ModelState.AddModelError(nameof(vm.GroupCode), "A group with this code already exists.");
            }

            if (!ModelState.IsValid) return View(vm);

            var group = new CourtGroup
            {
                GroupCode = vm.GroupCode.Trim(),
                GroupName = vm.GroupName.Trim(),
                Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim(),
                IsActive = vm.IsActive
            };
            _db.CourtGroups.Add(group);
            await _db.SaveChangesAsync();

            await AssignCourtsAsync(group.Id, vm.SelectedCourtIds);

            TempData["Success"] = $"Field group '{group.GroupName}' created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var group = await _db.CourtGroups
                .Include(g => g.Courts)
                .FirstOrDefaultAsync(g => g.Id == id);
            if (group == null) return NotFound();

            var vm = CourtGroupFormViewModel.From(group);
            vm.SelectedCourtIds = group.Courts.Select(c => c.Id).ToList();
            vm.CurrentMembers = group.Courts.OrderBy(c => c.CourtName).ToList();
            vm.AvailableCourts = await GetAssignableCourtsAsync(currentGroupId: id);
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CourtGroupFormViewModel vm)
        {
            await NormalizeCodeAsync(vm);
            vm.AvailableCourts = await GetAssignableCourtsAsync(currentGroupId: vm.Id);

            var group = await _db.CourtGroups.FindAsync(vm.Id);
            if (group == null) return NotFound();

            if (await _db.CourtGroups.AnyAsync(g => g.GroupCode == vm.GroupCode && g.Id != vm.Id))
            {
                ModelState.AddModelError(nameof(vm.GroupCode), "A group with this code already exists.");
            }

            if (!ModelState.IsValid)
            {
                vm.CurrentMembers = await _db.Courts.Where(c => c.CourtGroupId == vm.Id).OrderBy(c => c.CourtName).ToListAsync();
                return View(vm);
            }

            group.GroupCode = vm.GroupCode.Trim();
            group.GroupName = vm.GroupName.Trim();
            group.Description = string.IsNullOrWhiteSpace(vm.Description) ? null : vm.Description.Trim();
            group.IsActive = vm.IsActive;
            await _db.SaveChangesAsync();

            await AssignCourtsAsync(group.Id, vm.SelectedCourtIds);

            TempData["Success"] = $"Field group '{group.GroupName}' updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var group = await _db.CourtGroups.Include(g => g.Courts).FirstOrDefaultAsync(g => g.Id == id);
            if (group == null) return NotFound();

            // Detach members so they become independent (CourtGroupId = null).
            foreach (var c in group.Courts) c.CourtGroupId = null;
            _db.CourtGroups.Remove(group);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Field group '{group.GroupName}' deleted; member fields are now independent.";
            return RedirectToAction(nameof(Index));
        }

        // POST /Admin/CourtGroups/RemoveCourt/{groupId}?courtId=...
        // Detach a single member from the group without touching others.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveCourt(int groupId, int courtId)
        {
            var court = await _db.Courts.FirstOrDefaultAsync(c => c.Id == courtId && c.CourtGroupId == groupId);
            if (court == null) return NotFound();

            court.CourtGroupId = null;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"'{court.CourtName}' removed from the group.";
            return RedirectToAction(nameof(Edit), new { id = groupId });
        }

        // ---------- helpers ----------

        // Returns active courts that can be assigned to the current group. The
        // current group's existing members are always included so they remain
        // checked in the multi-select.
        private async Task<List<Court>> GetAssignableCourtsAsync(int? currentGroupId)
        {
            return await _db.Courts
                .Where(c => c.IsActive && (c.CourtGroupId == null || c.CourtGroupId == currentGroupId))
                .OrderBy(c => c.SportType)
                .ThenBy(c => c.CourtName)
                .ToListAsync();
        }

        // Sync the group's membership to exactly the given list of court IDs.
        // Anything previously assigned that isn't in the new list is detached.
        private async Task AssignCourtsAsync(int groupId, IEnumerable<int> selectedCourtIds)
        {
            var keep = new HashSet<int>(selectedCourtIds ?? Enumerable.Empty<int>());

            var currentMembers = await _db.Courts.Where(c => c.CourtGroupId == groupId).ToListAsync();
            foreach (var m in currentMembers)
            {
                if (!keep.Contains(m.Id)) m.CourtGroupId = null;
            }

            if (keep.Count > 0)
            {
                var toAdd = await _db.Courts
                    .Where(c => keep.Contains(c.Id) && c.CourtGroupId != groupId)
                    .ToListAsync();
                foreach (var c in toAdd) c.CourtGroupId = groupId;
            }

            await _db.SaveChangesAsync();
        }

        private static Task NormalizeCodeAsync(CourtGroupFormViewModel vm)
        {
            vm.GroupCode = (vm.GroupCode ?? string.Empty).Trim().ToUpperInvariant();
            return Task.CompletedTask;
        }
    }
}
