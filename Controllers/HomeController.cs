using System.Diagnostics;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly SiteContentService _content;
        private readonly ILogger<HomeController> _logger;

        public HomeController(ApplicationDbContext db, SiteContentService content, ILogger<HomeController> logger)
        {
            _db = db;
            _content = content;
            _logger = logger;
        }

        // Public landing page — visible to guests, members, staff, and admins.
        public async Task<IActionResult> Index()
        {
            var courts = await _db.Courts
                .Where(c => c.IsActive)
                .OrderBy(c => c.SportType)
                .ThenBy(c => c.CourtName)
                .ToListAsync();

            ViewBag.SportSummary = Enum.GetValues<SportType>()
                .Select(s => new
                {
                    Sport = s,
                    Count = courts.Count(c => c.SportType == s)
                })
                .ToList();

            // CMS-driven page content
            ViewBag.Settings = await _content.GetSettingsAsync();
            ViewBag.Hero = await _content.GetActiveHeroAsync();
            ViewBag.About = await _content.GetAboutAsync();
            ViewBag.Gallery = await _content.GetGalleryAsync();
            ViewBag.Promotions = await _content.GetActivePromotionsAsync(PhilippineTime.Today);

            return View(courts);
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }

    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
