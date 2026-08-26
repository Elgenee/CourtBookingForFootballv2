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
    public class AboutController : Controller
    {
        private readonly ApplicationDbContext _db;
        public AboutController(ApplicationDbContext db) { _db = db; }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var about = await _db.AboutContents.FirstOrDefaultAsync() ?? new AboutContent();
            return View(new AboutContentViewModel
            {
                Title = about.Title,
                Description = about.Description,
                FacilitiesList = about.FacilitiesList,
                ContactPhone = about.ContactPhone,
                ContactEmail = about.ContactEmail,
                Location = about.Location,
                OpeningHoursDays = about.OpeningHoursDays,
                OpeningHoursTimes = about.OpeningHoursTimes
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(AboutContentViewModel vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var about = await _db.AboutContents.FirstOrDefaultAsync();
            var isNew = about == null;
            about ??= new AboutContent();

            about.Title = vm.Title.Trim();
            about.Description = vm.Description.Trim();
            about.FacilitiesList = NormalizeFacilities(vm.FacilitiesList);
            about.ContactPhone = vm.ContactPhone?.Trim();
            about.ContactEmail = vm.ContactEmail?.Trim();
            about.Location = vm.Location?.Trim();
            about.OpeningHoursDays = vm.OpeningHoursDays?.Trim();
            about.OpeningHoursTimes = vm.OpeningHoursTimes?.Trim();
            about.UpdatedDate = DateTime.UtcNow;

            if (isNew) _db.AboutContents.Add(about);
            await _db.SaveChangesAsync();
            TempData["Success"] = "About section updated.";
            return RedirectToAction(nameof(Index));
        }

        private static string NormalizeFacilities(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().TrimEnd('\r'))
                .Where(l => l.Length > 0);
            return string.Join('\n', lines);
        }
    }
}
