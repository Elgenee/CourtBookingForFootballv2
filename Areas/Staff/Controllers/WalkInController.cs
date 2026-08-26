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
        private readonly CourtBookingSystem.Services.CourtGroupAvailabilityService _groupAvailability;
        private readonly CourtBookingSystem.Services.BookingPricingService _pricing;

        public WalkInController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            CourtBookingSystem.Services.CourtGroupAvailabilityService groupAvailability,
            CourtBookingSystem.Services.BookingPricingService pricing)
        {
            _db = db;
            _userManager = userManager;
            _groupAvailability = groupAvailability;
            _pricing = pricing;
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var vm = new WalkInFormViewModel
            {
                Courts = await GetCourtsAsync(),
                BookingDate = PhilippineTime.Today,
                DurationHours = 1,
                PaymentMethod = PaymentMethod.Cash
            };
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(WalkInFormViewModel vm)
        {
            vm.Courts = await GetCourtsAsync();

            if (!ModelState.IsValid) return View(vm);

            var court = vm.Courts.FirstOrDefault(c => c.Id == vm.CourtId);
            if (court == null)
            {
                ModelState.AddModelError(nameof(vm.CourtId), "Court not found.");
                return View(vm);
            }

            var now = PhilippineTime.Now;

            // Business hours
            var bh = await _db.BusinessHours.FirstOrDefaultAsync(b => b.DayOfWeek == vm.BookingDate.DayOfWeek);
            if (bh == null || bh.IsClosed)
            {
                ModelState.AddModelError(nameof(vm.BookingDate), "Facility is closed on this day.");
                return View(vm);
            }

            var duration = TimeSpan.FromHours(vm.DurationHours);
            var slotStartDateTime = OperatingHoursHelper.ToOperatingDateTime(vm.BookingDate, bh, vm.StartTime);
            var slotEndDateTime = slotStartDateTime.Add(duration);
            var endTime = slotEndDateTime.TimeOfDay;
            var operatingRange = OperatingHoursHelper.GetOperatingRange(vm.BookingDate, bh);

            if (slotStartDateTime < operatingRange.Start || slotEndDateTime > operatingRange.End)
            {
                ModelState.AddModelError(nameof(vm.StartTime),
                    $"Time must be within business hours ({PhilippineTime.FormatTime(bh.OpenTime)} – {PhilippineTime.FormatTime(bh.CloseTime)}).");
                return View(vm);
            }

            // Disallow past starts using the real slot date/time, including after-midnight operating slots.
            if (slotStartDateTime < now)
            {
                ModelState.AddModelError(nameof(vm.StartTime), "Start time cannot be in the past.");
                return View(vm);
            }

            // Conflict checks (materialize for cross-provider compat)
            var sameDay = await _db.Bookings
                .Where(b => b.CourtId == vm.CourtId
                    && b.BookingDate.Date == vm.BookingDate.Date
                    && b.BookingStatus != BookingStatus.Cancelled)
                .Select(b => new { b.StartTime, b.EndTime })
                .ToListAsync();
            if (sameDay
                .Select(b => OperatingHoursHelper.GetTimeWindow(vm.BookingDate, bh, b.StartTime, b.EndTime))
                .Any(b => OperatingHoursHelper.Overlaps(b.Start, b.End, slotStartDateTime, slotEndDateTime)))
            {
                ModelState.AddModelError(nameof(vm.StartTime), "This time slot conflicts with an existing booking.");
                return View(vm);
            }
            var blocks = await _db.BlockedSlots
                .Where(s => s.CourtId == vm.CourtId && s.BlockedDate.Date == vm.BookingDate.Date)
                .Select(s => new { s.StartTime, s.EndTime })
                .ToListAsync();
            if (blocks
                .Select(s => OperatingHoursHelper.GetTimeWindow(vm.BookingDate, bh, s.StartTime, s.EndTime))
                .Any(s => OperatingHoursHelper.Overlaps(s.Start, s.End, slotStartDateTime, slotEndDateTime)))
            {
                ModelState.AddModelError(nameof(vm.StartTime), "This time slot is blocked.");
                return View(vm);
            }

            // Court Group / shared physical area conflict
            var groupConflicts = await _groupAvailability.GetGroupConflictsAsync(vm.CourtId, vm.BookingDate);
            var groupClash = CourtBookingSystem.Services.CourtGroupAvailabilityService
                .FindOverlap(groupConflicts, slotStartDateTime, slotEndDateTime);
            if (groupClash != null)
            {
                ModelState.AddModelError(nameof(vm.StartTime), groupClash.Reason);
                return View(vm);
            }

            var user = await _userManager.GetUserAsync(User);
            var price = _pricing.Quote(court, vm.BookingDate, vm.StartTime, vm.DurationHours);
            var totalAmount = price.TotalAmount;
            var pricingLabel = price.IsMixedRate
                ? "mixed rates"
                : price.IsPromoApplied ? "promo rate" : "regular rate";
            var refNo = $"RC-{PhilippineTime.Today:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

            // Walk-ins typically lack email; auto-generate a placeholder if blank.
            var email = string.IsNullOrWhiteSpace(vm.CustomerEmail)
                ? $"walkin-{refNo.ToLowerInvariant()}@courtbook.local"
                : vm.CustomerEmail.Trim();

            // Payment flow differs by method:
            //   - Cash: paid at the counter → booking is instantly Confirmed and
            //     payment is auto-Approved.
            //   - Manual GCash: customer needs to send money and staff must
            //     upload the GCash receipt before payment can be verified. We
            //     reuse the existing customer flow (Pending booking + Unpaid
            //     payment + /Bookings/UploadProof endpoint + Confirmation page)
            //     so the proof upload, storage, validation and verification
            //     queue all work without duplication.
            var isCash = vm.PaymentMethod == PaymentMethod.Cash;

            var booking = new Booking
            {
                BookingReferenceNo = refNo,
                UserId = null,
                CustomerName = vm.CustomerName.Trim(),
                CustomerEmail = email,
                CustomerMobile = vm.CustomerMobile.Trim(),
                CourtId = vm.CourtId,
                BookingDate = vm.BookingDate.Date,
                StartTime = vm.StartTime,
                EndTime = endTime,
                TotalAmount = totalAmount,
                BookingStatus = isCash ? BookingStatus.Confirmed : BookingStatus.Pending,
                Notes = string.IsNullOrWhiteSpace(vm.Notes) ? "Walk-in (counter)" : "Walk-in (counter) — " + vm.Notes.Trim(),
                CreatedDate = DateTime.UtcNow
            };

            var payment = new Payment
            {
                Booking = booking,
                Amount = totalAmount,
                PaymentMethod = vm.PaymentMethod,
                PaymentStatus = isCash ? PaymentStatus.Approved : PaymentStatus.Unpaid,
                PaidDate = isCash ? DateTime.UtcNow : null,
                ConfirmedByUserId = isCash ? user?.Id : null,
                ConfirmedDate = isCash ? DateTime.UtcNow : null
            };

            _db.Bookings.Add(booking);
            _db.Payments.Add(payment);
            await _db.SaveChangesAsync();

            if (isCash)
            {
                TempData["Success"] = $"Walk-in booking {refNo} created for {vm.CustomerName} ({totalAmount:0.00}, {pricingLabel}).";
                return RedirectToAction("Today", "Bookings");
            }

            // Manual GCash walk-in: hand off to the shared customer Confirmation
            // page, which already renders the GCash payment instructions and the
            // proof-upload form (POSTs to /Bookings/UploadProof). Once proof is
            // uploaded the booking moves to ForApproval and shows up in the
            // existing /Staff/Payments verification queue.
            TempData["Success"] = $"Walk-in booking {refNo} created ({totalAmount:0.00}, {pricingLabel}). Upload the GCash receipt.";
            return RedirectToAction("Details", "Bookings", new { area = "Staff", id = booking.Id });
        }

        private Task<List<Court>> GetCourtsAsync() =>
            _db.Courts
                .Where(c => c.IsActive && c.Status != CourtStatus.Closed && c.Status != CourtStatus.UnderMaintenance)
                .OrderBy(c => c.SportType)
                .ThenBy(c => c.CourtName)
                .ToListAsync();
    }
}
