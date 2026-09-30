using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public PaymentsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(PaymentStatus? status)
        {
            var q = _db.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Court)
                .Include(p => p.ConfirmedByUser)
                .AsQueryable();

            if (status.HasValue) q = q.Where(p => p.PaymentStatus == status.Value);

            var payments = await q
                .OrderByDescending(p => p.Id)
                .Take(200)
                .ToListAsync();

            ViewBag.FilterStatus = status;
            return View(payments);
        }

        // Approve a payment proof. Action name kept as "Approve" to match the
        // simplified workflow (Unpaid → Submitted → Approved / Rejected).
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            var payment = await _db.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return NotFound();

            if (payment.PaymentMethod == PaymentMethod.PayMongoQrPh)
            {
                TempData["Error"] = $"PayMongo payment for booking {payment.Booking?.BookingReferenceNo} must be confirmed by webhook.";
                return RedirectToAction(nameof(Index));
            }

            if (payment.PaymentStatus is PaymentStatus.Approved or PaymentStatus.Rejected)
            {
                TempData["Error"] = $"Payment for booking {payment.Booking?.BookingReferenceNo} is already {payment.PaymentStatus} and cannot be changed.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.GetUserAsync(User);

            payment.PaymentStatus = PaymentStatus.Approved;
            payment.PaidDate ??= DateTime.UtcNow;
            payment.ConfirmedByUserId = user?.Id;
            payment.ConfirmedDate = DateTime.UtcNow;

            // Approving a payment confirms the booking regardless of whether it
            // was sitting in Pending (no proof yet) or ForApproval (proof under review).
            if (payment.Booking != null &&
                (payment.Booking.BookingStatus == BookingStatus.Pending
                 || payment.Booking.BookingStatus == BookingStatus.ForApproval))
            {
                payment.Booking.BookingStatus = BookingStatus.Confirmed;
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Payment for booking {payment.Booking?.BookingReferenceNo} approved.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id)
        {
            var payment = await _db.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return NotFound();

            if (payment.PaymentMethod == PaymentMethod.PayMongoQrPh)
            {
                TempData["Error"] = $"PayMongo payment for booking {payment.Booking?.BookingReferenceNo} must be updated by webhook.";
                return RedirectToAction(nameof(Index));
            }

            if (payment.PaymentStatus is PaymentStatus.Approved or PaymentStatus.Rejected)
            {
                TempData["Error"] = $"Payment for booking {payment.Booking?.BookingReferenceNo} is already {payment.PaymentStatus} and cannot be changed.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.GetUserAsync(User);

            payment.PaymentStatus = PaymentStatus.Rejected;
            payment.ConfirmedByUserId = user?.Id;
            payment.ConfirmedDate = DateTime.UtcNow;

            // Reject pushes the booking back to Pending so the customer can re-upload.
            if (payment.Booking != null && payment.Booking.BookingStatus == BookingStatus.ForApproval)
            {
                payment.Booking.BookingStatus = BookingStatus.Pending;
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Payment for booking {payment.Booking?.BookingReferenceNo} rejected.";
            return RedirectToAction(nameof(Index));
        }
    }
}
