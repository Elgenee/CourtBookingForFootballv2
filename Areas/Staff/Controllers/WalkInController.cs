using System.Security.Cryptography;
using System.Text;
using CourtBookingSystem.Areas.Staff.ViewModels;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff,Admin")]
    public class WalkInController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly BookingAvailabilityService _availability;
        private readonly BookingPricingService _pricing;

        public WalkInController(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
            BookingAvailabilityService availability, BookingPricingService pricing)
        {
            _db = db;
            _userManager = userManager;
            _availability = availability;
            _pricing = pricing;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            return View(new WalkInFormViewModel
            {
                Courts = await GetCourtsAsync(),
                BookingDate = PhilippineTime.Today,
                DurationHours = 1,
                PaymentMethod = PaymentMethod.Cash
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        [TypeFilter(typeof(BookingCreationTransactionFilter))]
        public async Task<IActionResult> Create(WalkInFormViewModel vm)
        {
            vm.Courts = await GetCourtsAsync();
            if (vm.PaymentMethod != PaymentMethod.Cash && vm.PaymentMethod != PaymentMethod.GCash)
                ModelState.AddModelError(nameof(vm.PaymentMethod), "Choose Cash or Manual GCash.");
            if (vm.UseSlotSelection && vm.Slots.Count == 0)
                ModelState.AddModelError("", "Add at least one slot to the booking summary.");
            if (vm.SubmissionId == Guid.Empty)
                ModelState.AddModelError("", "This booking form has expired. Reload the page and try again.");

            // Accept the previous single-slot form contract for older callers.
            if (!vm.UseSlotSelection && vm.Slots.Count == 0)
                vm.Slots.Add(new WalkInSlotViewModel
                {
                    CourtId = vm.CourtId, BookingDate = vm.BookingDate,
                    StartTime = vm.StartTime, DurationHours = vm.DurationHours
                });

            if (vm.Slots.Count > 100)
            {
                ModelState.AddModelError("", "Choose at most 100 bookings at a time.");
                return View(vm);
            }
            var validRequest = ModelState.IsValid;
            // The unique reference index provides durable replay protection, even
            // after cancellation, an application restart, or a second browser POST.
            var references = vm.Slots.Select((slot, index) => Reference(vm.SubmissionId, slot, index)).ToList();
            var existing = validRequest && references.Count > 0
                ? await _db.Bookings.Where(b => references.Contains(b.BookingReferenceNo)).ToListAsync()
                : new List<Booking>();
            if (existing.Count > 0)
            {
                TempData["Success"] = "This walk-in submission has already been created.";
                return RedirectToAction(nameof(Created), new { ids = string.Join(",", existing.Select(b => b.Id)) });
            }

            await PopulateAndValidateSlotsAsync(vm, validRequest);
            if (!ModelState.IsValid) return View(vm);

            var user = await _userManager.GetUserAsync(User);
            var isCash = vm.PaymentMethod == PaymentMethod.Cash;
            var created = new List<Booking>();
            for (var index = 0; index < vm.Slots.Count; index++)
            {
                var slot = vm.Slots[index];
                var reference = references[index];
                var booking = new Booking
                {
                    BookingReferenceNo = reference,
                    UserId = null,
                    CustomerName = vm.CustomerName.Trim(),
                    CustomerEmail = string.IsNullOrWhiteSpace(vm.CustomerEmail)
                        ? $"walkin-{reference.ToLowerInvariant()}@giuseppefootball.local" : vm.CustomerEmail.Trim(),
                    CustomerMobile = vm.CustomerMobile.Trim(),
                    CourtId = slot.CourtId,
                    BookingDate = slot.BookingDate!.Value.Date,
                    StartTime = slot.StartTime!.Value,
                    EndTime = slot.EndDateTime!.Value.TimeOfDay,
                    TotalAmount = slot.TotalAmount,
                    BookingStatus = isCash ? BookingStatus.Confirmed : BookingStatus.Pending,
                    Notes = string.IsNullOrWhiteSpace(vm.Notes) ? "Walk-in (counter)" : "Walk-in (counter) — " + vm.Notes.Trim(),
                    CreatedDate = DateTime.UtcNow
                };
                _db.Bookings.Add(booking);
                _db.Payments.Add(new Payment
                {
                    Booking = booking,
                    Amount = booking.TotalAmount,
                    PaymentMethod = vm.PaymentMethod,
                    PaymentStatus = isCash ? PaymentStatus.Approved : PaymentStatus.Unpaid,
                    PaidDate = isCash ? DateTime.UtcNow : null,
                    ConfirmedByUserId = isCash ? user?.Id : null,
                    ConfirmedDate = isCash ? DateTime.UtcNow : null
                });
                created.Add(booking);
            }
            await _db.SaveChangesAsync();

            var total = created.Sum(b => b.TotalAmount);
            TempData["Success"] = $"{created.Count} walk-in booking(s) created for {vm.CustomerName} (₱{total:N2})."
                + (isCash ? "" : " Upload the GCash receipt for each booking.");
            if (created.Count > 1)
                return RedirectToAction(nameof(Created), new { ids = string.Join(",", created.Select(b => b.Id)) });
            return isCash ? RedirectToAction("Today", "Bookings")
                : RedirectToAction("Details", "Bookings", new { area = "Staff", id = created[0].Id });
        }

        [HttpGet]
        public async Task<IActionResult> Created(string ids)
        {
            var bookingIds = (ids ?? "").Split(',').Take(100)
                .Select(value => int.TryParse(value, out var id) ? id : 0).ToArray();
            var bookings = await _db.Bookings.AsNoTracking().Include(b => b.Court).Include(b => b.Payments)
                .Where(b => bookingIds.Contains(b.Id)).OrderBy(b => b.BookingDate).ToListAsync();
            if (bookings.Count == 0) return NotFound();
            ViewData["OperatingHours"] = await _db.BusinessHours.AsNoTracking().ToDictionaryAsync(h => h.DayOfWeek);
            return View(bookings);
        }

        private async Task PopulateAndValidateSlotsAsync(WalkInFormViewModel vm, bool validate)
        {
            var hours = await _db.BusinessHours.AsNoTracking().ToListAsync();
            var windows = new List<(Court Court, DateTime Start, DateTime End)>();
            var availability = new Dictionary<(int, DateTime, int), List<BookingAvailabilityService.TimeSlot>>();
            foreach (var slot in vm.Slots)
            {
                slot.Error = null;
                var court = vm.Courts.FirstOrDefault(c => c.Id == slot.CourtId);
                slot.CourtName = court?.CourtName ?? "Unavailable field";
                slot.Sport = court?.SportType.ToString() ?? "";
                var bh = hours.FirstOrDefault(h => h.DayOfWeek == slot.BookingDate?.DayOfWeek);
                if (court == null) slot.Error = "The field is no longer available.";
                else if (!slot.BookingDate.HasValue || slot.BookingDate.Value.Date < PhilippineTime.Today.AddDays(-1)
                    || slot.BookingDate.Value.Date > PhilippineTime.Today.AddYears(5))
                    slot.Error = "Choose an operating date from yesterday through the next five years.";
                else if (!slot.StartTime.HasValue || slot.StartTime < TimeSpan.Zero || slot.StartTime >= TimeSpan.FromDays(1)
                    || slot.DurationHours < 1 || slot.DurationHours > 8)
                    slot.Error = "Choose a valid start time and a duration between 1 and 8 hours.";
                else if (bh == null || bh.IsClosed) slot.Error = "The facility is closed on this operating date.";
                else
                {
                    var date = slot.BookingDate.Value.Date;
                    var start = OperatingHoursHelper.ToOperatingDateTime(date, bh, slot.StartTime.Value);
                    var end = start.AddHours(slot.DurationHours);
                    slot.StartDateTime = start;
                    slot.EndDateTime = end;
                    var quote = _pricing.Quote(court, date, slot.StartTime.Value, slot.DurationHours);
                    slot.TotalAmount = quote.TotalAmount;
                    slot.RateType = quote.IsMixedRate ? "Mixed rates" : quote.IsPromoApplied ? "Promo" : "Regular";
                    if (validate)
                    {
                        var key = (court.Id, date, slot.DurationHours);
                        if (!availability.TryGetValue(key, out var slots))
                            availability[key] = slots = await _availability.GenerateSlotsForCourtAsync(court.Id, date, slot.DurationHours);
                        var candidate = slots.FirstOrDefault(s => s.StartDateTime == start);
                        if (candidate == null) slot.Error = "The time is outside the available operating slots.";
                        else if (!candidate.IsAvailable) slot.Error = candidate.Reason ?? "The slot is unavailable.";
                        else if (windows.Any(w => SharesResource(w.Court, court)
                            && OperatingHoursHelper.Overlaps(w.Start, w.End, start, end)))
                            slot.Error = "Duplicate or overlapping selection for the same field or shared field.";
                        windows.Add((court, start, end));
                    }
                }
                if (slot.Error != null && validate)
                    ModelState.AddModelError("", $"{slot.CourtName}, {slot.BookingDate:MMM d, yyyy}, {slot.StartTime:hh\\:mm} ({slot.DurationHours} hour(s)): {slot.Error}");
            }
        }

        private static bool SharesResource(Court first, Court second) => first.Id == second.Id
            || (first.CourtGroupId.HasValue && first.CourtGroupId == second.CourtGroupId && (first.IsFullCourt || second.IsFullCourt));

        private static string Reference(Guid submissionId, WalkInSlotViewModel slot, int index)
        {
            var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{submissionId:N}:{index}")))[..16];
            return $"GFC-{slot.BookingDate:yyyyMMdd}-{suffix}";
        }

        private Task<List<Court>> GetCourtsAsync() => _db.Courts
            .Where(c => c.IsActive && c.Status != CourtStatus.Closed && c.Status != CourtStatus.UnderMaintenance)
            .OrderBy(c => c.SportType).ThenBy(c => c.CourtName).ToListAsync();
    }
}
