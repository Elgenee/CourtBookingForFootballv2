using CourtBookingSystem.Areas.Admin.ViewModels;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CourtBookingSystem.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff,Admin")]
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BookingsController> _logger;

        public BookingsController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment env,
            ILogger<BookingsController> logger)
        {
            _db = db;
            _userManager = userManager;
            _env = env;
            _logger = logger;
        }

        // /Staff/Bookings — full bookings list with filters. Mirrors the
        // Admin bookings list (same ViewModel and query) so Staff and Admin
        // see consistent data; only the actions available to Staff differ.
        public async Task<IActionResult> Index(BookingFilterViewModel filter)
        {
            filter.Courts = await _db.Courts.OrderBy(c => c.CourtName).ToListAsync();

            var q = _db.Bookings.Include(b => b.Court).Include(b => b.Payments).AsQueryable();

            if (filter.FromDate.HasValue)
                q = q.Where(b => b.BookingDate >= filter.FromDate.Value.Date);
            if (filter.ToDate.HasValue)
                q = q.Where(b => b.BookingDate <= filter.ToDate.Value.Date);
            if (filter.CourtId.HasValue)
                q = q.Where(b => b.CourtId == filter.CourtId.Value);
            if (filter.Sport.HasValue)
                q = q.Where(b => b.Court!.SportType == filter.Sport.Value);
            if (filter.Status.HasValue)
                q = q.Where(b => b.BookingStatus == filter.Status.Value);
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim();
                q = q.Where(b => b.BookingReferenceNo.Contains(s)
                              || b.CustomerName.Contains(s)
                              || b.CustomerEmail.Contains(s));
            }

            // Materialize before sorting by TimeSpan (same approach used by Admin).
            var raw = await q
                .OrderByDescending(b => b.BookingDate)
                .Take(200)
                .ToListAsync();
            filter.Results = raw
                .OrderByDescending(b => b.BookingDate)
                .ThenBy(b => b.StartTime)
                .ToList();

            return View(filter);
        }

        public async Task<IActionResult> Calendar(string? month, int? courtId, SportType? sport, BookingStatus? status)
        {
            var model = await BuildCalendarViewModelAsync(month, courtId, sport, status);
            return View(model);
        }

        // /Staff/Bookings/Details/{id} — Staff-facing booking detail. Same
        // information as the Admin detail page; the action buttons exposed to
        // Staff are: Approve / Reject Manual GCash payment, Cancel booking,
        // and (when appropriate) Mark Completed.
        public async Task<IActionResult> Details(int id)
        {
            var booking = await _db.Bookings
                .Include(b => b.Court)
                .Include(b => b.Payments)
                    .ThenInclude(p => p.ConfirmedByUser)
                .FirstOrDefaultAsync(b => b.Id == id);
            if (booking == null) return NotFound();
            return View(booking);
        }

        // /Staff/Bookings/Today
        public async Task<IActionResult> Today(DateTime? date)
        {
            var target = (date ?? PhilippineTime.Today).Date;

            // Auto-complete past Confirmed bookings on view load so dashboards
            // and lists reflect the simplified status workflow.
            await CourtBookingSystem.Services.BookingStatusHelper.AutoCompletePastBookingsAsync(_db);

            var bookings = await _db.Bookings
                .Include(b => b.Court)
                .Include(b => b.Payments)
                .Where(b => b.BookingDate.Date == target)
                .ToListAsync();

            ViewBag.TargetDate = target;
            return View(bookings.OrderBy(b => b.StartTime).ToList());
        }

        // POST /Staff/Bookings/ApprovePayment — verifies any pending GCash/Cash
        // payments on the booking and moves the booking to Confirmed.
        // Optional returnTo=details keeps the user on the Booking Detail page;
        // otherwise we redirect to Today (the original behaviour used by the
        // Today's Bookings list).
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ApprovePayment(int id, string? returnTo = null)
        {
            var booking = await _db.Bookings
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.Id == id);
            if (booking == null) return NotFound();

            if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Completed)
            {
                TempData["Error"] = $"Cannot approve payment for {booking.BookingReferenceNo} (status: {booking.BookingStatus}).";
                return RedirectAfterAction(returnTo, id);
            }

            var user = await _userManager.GetUserAsync(User);

            var touched = false;
            foreach (var p in booking.Payments)
            {
                if (p.PaymentMethod == PaymentMethod.PayMongoQrPh) continue;
                if (p.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Submitted or PaymentStatus.Rejected)
                {
                    p.PaymentStatus = PaymentStatus.Approved;
                    p.PaidDate ??= DateTime.UtcNow;
                    p.ConfirmedByUserId = user?.Id;
                    p.ConfirmedDate = DateTime.UtcNow;
                    touched = true;
                }
            }

            if (!touched)
            {
                TempData["Error"] = $"No manual payment to approve on {booking.BookingReferenceNo}. PayMongo payments are webhook-driven.";
                return RedirectAfterAction(returnTo, id);
            }

            booking.BookingStatus = BookingStatus.Confirmed;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Payment approved and {booking.BookingReferenceNo} confirmed.";
            return RedirectAfterAction(returnTo, id);
        }

        // POST /Staff/Bookings/RejectPayment — rejects all not-yet-approved
        // payment proofs on the booking and pushes the booking back to Pending
        // so the customer/staff can re-upload a corrected proof. Only meant for
        // Manual GCash; PayMongo QR Ph is webhook-driven and never enters this
        // flow.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectPayment(int id, string? returnTo = null)
        {
            var booking = await _db.Bookings
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.Id == id);
            if (booking == null) return NotFound();

            if (booking.BookingStatus is BookingStatus.Cancelled or BookingStatus.Completed)
            {
                TempData["Error"] = $"Cannot reject payment for {booking.BookingReferenceNo} (status: {booking.BookingStatus}).";
                return RedirectAfterAction(returnTo, id);
            }

            var user = await _userManager.GetUserAsync(User);
            var touched = false;

            foreach (var p in booking.Payments)
            {
                // Don't touch already-Approved payments or PayMongo QR Ph
                // (auto-confirmed by webhook — must not be manually rejected).
                if (p.PaymentMethod == PaymentMethod.PayMongoQrPh) continue;
                if (p.PaymentStatus is PaymentStatus.Submitted or PaymentStatus.Unpaid)
                {
                    p.PaymentStatus = PaymentStatus.Rejected;
                    p.ConfirmedByUserId = user?.Id;
                    p.ConfirmedDate = DateTime.UtcNow;
                    touched = true;
                }
            }

            if (!touched)
            {
                TempData["Error"] = $"No pending payment proof to reject on {booking.BookingReferenceNo}.";
                return RedirectAfterAction(returnTo, id);
            }

            // Reject pushes the booking back to Pending so a corrected proof
            // can be re-uploaded (matches the customer GCash flow used by
            // Admin/PaymentsController.Reject).
            booking.BookingStatus = BookingStatus.Pending;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Payment for {booking.BookingReferenceNo} rejected; booking moved back to Pending.";
            return RedirectAfterAction(returnTo, id);
        }

        // POST /Staff/Bookings/Complete — manual override for finished bookings.
        // The dashboard auto-completes past Confirmed bookings, but staff can
        // mark one finished early if needed.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(int id, string? returnTo = null)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
            if (booking == null) return NotFound();

            if (booking.BookingStatus != BookingStatus.Confirmed)
            {
                TempData["Error"] = $"Only Confirmed bookings can be marked Completed (current: {booking.BookingStatus}).";
                return RedirectAfterAction(returnTo, id);
            }

            booking.BookingStatus = BookingStatus.Completed;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Booking {booking.BookingReferenceNo} marked as completed.";
            return RedirectAfterAction(returnTo, id);
        }

        // POST /Staff/Bookings/Cancel — Staff/Admin can cancel a booking.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, string? returnTo = null)
        {
            var booking = await _db.Bookings.FirstOrDefaultAsync(b => b.Id == id);
            if (booking == null) return NotFound();

            if (booking.BookingStatus is BookingStatus.Completed or BookingStatus.Cancelled)
            {
                TempData["Error"] = $"Booking {booking.BookingReferenceNo} cannot be cancelled (status: {booking.BookingStatus}).";
                return RedirectAfterAction(returnTo, id);
            }

            booking.BookingStatus = BookingStatus.Cancelled;
            await _db.SaveChangesAsync();
            //await CourtBookingSystem.Services.NotificationService.NotifyAsync(_db,
            //    title: "Booking Cancelled",
            //    message: $"Booking {booking.BookingReferenceNo} has been cancelled.",
            //    link: $"/Staff/Bookings/Details/{booking.Id}",
            //    relatedBookingId: booking.Id);
            TempData["Success"] = $"Booking {booking.BookingReferenceNo} cancelled.";
            return RedirectAfterAction(returnTo, id);
        }

        // POST /Bookings/UploadProof
        // Public — booking reference acts as the soft secret. Accepts GCash receipt images.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6_000_000)]
        public async Task<IActionResult> UploadProof(string reference, IFormFile? proof)
        {
            if (string.IsNullOrWhiteSpace(reference)) return NotFound();

            var booking = await _db.Bookings
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingReferenceNo == reference);
            if (booking == null) return NotFound();

            // Find the GCash payment on this booking
            var payment = booking.Payments
                .Where(p => p.PaymentMethod == PaymentMethod.GCash)
                .OrderByDescending(p => p.Id)
                .FirstOrDefault();
            if (payment == null)
            {
                TempData["UploadError"] = "This booking does not require a payment proof upload.";
                return RedirectToAction(nameof(Details), new { id = booking.Id });
            }
            if (payment.PaymentStatus == PaymentStatus.Approved)
            {
                TempData["UploadError"] = "Payment has already been approved.";
                return RedirectToAction(nameof(Details), new { id = booking.Id });
            }

            // ----- Validate file -----
            if (proof == null || proof.Length == 0)
            {
                TempData["UploadError"] = "Please choose an image file to upload.";
                return RedirectToAction(nameof(Details), new { id = booking.Id });
            }
            if (proof.Length > 5 * 1024 * 1024)
            {
                TempData["UploadError"] = "Image is too large. Maximum allowed is 5 MB.";
                return RedirectToAction(nameof(Details), new { id = booking.Id });
            }

            var allowedMime = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedMime.Contains(proof.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                TempData["UploadError"] = "Only JPEG, PNG, or WebP images are allowed.";
                return RedirectToAction(nameof(Details), new { id = booking.Id });
            }

            var ext = Path.GetExtension(proof.FileName).ToLowerInvariant();
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(ext))
            {
                ext = proof.ContentType.ToLowerInvariant() switch
                {
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    _ => ".jpg"
                };
            }

            // ----- Save file -----
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(webRoot, "uploads", "payments");
            Directory.CreateDirectory(folder);

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(folder, fileName);
            using (var stream = System.IO.File.Create(fullPath))
            {
                await proof.CopyToAsync(stream);
            }

            // Delete previous proof file (if customer re-uploads)
            if (!string.IsNullOrEmpty(payment.ProofImagePath))
            {
                var old = Path.Combine(webRoot, payment.ProofImagePath.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(old))
                {
                    try { System.IO.File.Delete(old); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete old proof file {Path}", old); }
                }
            }

            payment.ProofImagePath = $"uploads/payments/{fileName}";
            payment.PaymentStatus = PaymentStatus.Submitted;
            payment.PaidDate ??= DateTime.UtcNow;

            // Customer uploaded payment proof — move booking into the
            // approval queue (covers both first-time uploads from Pending and
            // re-uploads after a Rejected verdict that reset the booking).
            if (booking.BookingStatus == BookingStatus.Pending)
            {
                booking.BookingStatus = BookingStatus.ForApproval;
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Proof uploaded for booking {Ref}: {File}", reference, fileName);

            // Notify admins/staff that this booking now has proof awaiting verification.

            TempData["UploadSuccess"] = "Payment proof uploaded! Our team will verify it shortly.";
            return RedirectToAction(nameof(Details), new { id = booking.Id });
        }

        private async Task<BookingCalendarViewModel> BuildCalendarViewModelAsync(
            string? month,
            int? courtId,
            SportType? sport,
            BookingStatus? status)
        {
            if (status == BookingStatus.Cancelled)
                status = null;

            var targetMonth = ParseCalendarMonth(month);
            var monthStart = new DateTime(targetMonth.Year, targetMonth.Month, 1);
            var monthEnd = monthStart.AddMonths(1);
            var calendarStart = monthStart.AddDays(-(int)monthStart.DayOfWeek).Date;
            var lastMonthDay = monthEnd.AddDays(-1);
            var calendarEnd = lastMonthDay.AddDays(6 - (int)lastMonthDay.DayOfWeek).Date;
            var exclusiveEnd = calendarEnd.AddDays(1);

            var q = _db.Bookings
                .Include(b => b.Court)
                .Include(b => b.Payments)
                .Where(b => b.BookingDate >= calendarStart && b.BookingDate < exclusiveEnd)
                .Where(b => b.BookingStatus != BookingStatus.Cancelled)
                .AsQueryable();

            if (courtId.HasValue)
                q = q.Where(b => b.CourtId == courtId.Value);
            if (sport.HasValue)
                q = q.Where(b => b.Court!.SportType == sport.Value);
            if (status.HasValue)
                q = q.Where(b => b.BookingStatus == status.Value);

            var bookings = await q
                .OrderBy(b => b.BookingDate)
                .ToListAsync();

            var grouped = bookings
                .GroupBy(b => b.BookingDate.Date)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(b => b.StartTime).ThenBy(b => b.Court?.CourtName ?? "").ToList());

            var days = new List<BookingCalendarDay>();
            for (var date = calendarStart; date <= calendarEnd; date = date.AddDays(1))
            {
                days.Add(new BookingCalendarDay
                {
                    Date = date,
                    IsCurrentMonth = date.Month == monthStart.Month && date.Year == monthStart.Year,
                    Bookings = grouped.TryGetValue(date, out var dayBookings) ? dayBookings : new List<Booking>()
                });
            }

            return new BookingCalendarViewModel
            {
                Month = monthStart,
                CourtId = courtId,
                Sport = sport,
                Status = status,
                Courts = await _db.Courts.OrderBy(c => c.CourtName).ToListAsync(),
                Days = days
            };
        }

        private static DateTime ParseCalendarMonth(string? month)
        {
            if (!string.IsNullOrWhiteSpace(month)
                && DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            var today = PhilippineTime.Today;
            return new DateTime(today.Year, today.Month, 1);
        }

        // Centralised post-action redirect: detail page calls pass returnTo=details
        // to stay on the Booking Detail screen; everything else (today's list,
        // notification links) falls back to the original Today redirect.
        private IActionResult RedirectAfterAction(string? returnTo, int id) =>
            string.Equals(returnTo, "details", StringComparison.OrdinalIgnoreCase)
                ? RedirectToAction(nameof(Details), new { id })
                : RedirectToAction(nameof(Today));
    }
}
