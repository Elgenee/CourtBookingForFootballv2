using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using Microsoft.AspNetCore.Mvc;

namespace CourtBookingSystem.Controllers
{
    public class AvailabilityController : Controller
    {
        private readonly BookingAvailabilityService _availability;

        public AvailabilityController(BookingAvailabilityService availability) => _availability = availability;

        // GET /Availability/Find
        [HttpGet]
        public IActionResult Find(SportType? sport, DateTime? date, int? durationHours)
        {
            ViewBag.Sport = sport ?? SportType.Pickleball;
            ViewBag.Date = (date ?? PhilippineTime.Today).ToString("yyyy-MM-dd");
            ViewBag.Duration = durationHours.HasValue && durationHours.Value is >= 1 and <= 8 ? durationHours.Value : 1;
            ViewBag.MinDate = PhilippineTime.Today.ToString("yyyy-MM-dd");
            ViewBag.MaxDate = PhilippineTime.Today.AddYears(5).ToString("yyyy-MM-dd");
            return View();
        }

        // GET /Availability/Search?sport=&date=&durationHours=
        [HttpGet]
        public async Task<IActionResult> Search(SportType sport, DateTime date, int durationHours = 1)
        {
            if (durationHours is < 1 or > 8) return BadRequest("Invalid duration.");
            var rows = await _availability.FindAvailableAsync(sport, date, durationHours);

            return Json(new
            {
                count = rows.Count,
                slots = rows.Select(r => new
                {
                    courtId = r.CourtId,
                    courtName = r.CourtName,
                    sportType = r.SportType.ToString(),
                    date = r.Date.ToString("yyyy-MM-dd"),
                    startTime = r.StartTime.ToString(@"hh\:mm\:ss"),
                    endTime = r.EndTime.ToString(@"hh\:mm\:ss"),
                    startLabel = PhilippineTime.FormatTime(r.StartTime),
                    endLabel = PhilippineTime.FormatTime(r.EndTime),
                    hourlyRate = r.HourlyRate,
                    totalAmount = r.TotalAmount,
                    isPromoRate = r.IsPromoRate,
                    isMixedRate = r.IsMixedRate
                })
            });
        }
    }
}
