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
    public class BusinessHoursController : Controller
    {
        private readonly ApplicationDbContext _db;

        public BusinessHoursController(ApplicationDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var existing = await _db.BusinessHours.ToListAsync();

            // Ensure all 7 days exist (idempotent).
            foreach (var d in Enum.GetValues<DayOfWeek>())
            {
                if (!existing.Any(x => x.DayOfWeek == d))
                {
                    _db.BusinessHours.Add(new BusinessHour
                    {
                        DayOfWeek = d,
                        OpenTime = new TimeSpan(8, 0, 0),
                        CloseTime = new TimeSpan(22, 0, 0),
                        IsClosed = false
                    });
                }
            }
            await _db.SaveChangesAsync();
            existing = await _db.BusinessHours.OrderBy(b => b.DayOfWeek).ToListAsync();

            var vm = new BusinessHoursViewModel
            {
                Days = existing.Select(b => new BusinessHoursViewModel.BusinessHourRow
                {
                    Id = b.Id,
                    DayOfWeek = b.DayOfWeek,
                    OpenTime = b.OpenTime,
                    CloseTime = b.CloseTime,
                    IsClosed = b.IsClosed
                }).ToList()
            };
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(BusinessHoursViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var existing = await _db.BusinessHours.ToListAsync();
            foreach (var row in vm.Days)
            {
                var bh = existing.FirstOrDefault(b => b.Id == row.Id);
                if (bh == null) continue;
                bh.OpenTime = row.OpenTime;
                bh.CloseTime = row.CloseTime;
                bh.IsClosed = row.IsClosed;
            }
            await _db.SaveChangesAsync();
            TempData["Success"] = "Business hours updated.";
            return RedirectToAction(nameof(Index));
        }
    }
}
