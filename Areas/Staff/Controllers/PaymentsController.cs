using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff,Admin")]
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public PaymentsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var payments = await _db.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Court)
                .Where(p => p.PaymentStatus == PaymentStatus.Unpaid || p.PaymentStatus == PaymentStatus.Submitted)
                .OrderByDescending(p => p.Id)
                .Take(100)
                .ToListAsync();
            return View(payments);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            var payment = await _db.Payments.Include(p => p.Booking).FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            payment.PaymentStatus = PaymentStatus.Approved;
            payment.PaidDate ??= DateTime.UtcNow;
            payment.ConfirmedByUserId = user?.Id;
            payment.ConfirmedDate = DateTime.UtcNow;
            if (payment.Booking != null &&
                (payment.Booking.BookingStatus == BookingStatus.Pending
                 || payment.Booking.BookingStatus == BookingStatus.ForApproval))
            {
                payment.Booking.BookingStatus = BookingStatus.Confirmed;
            }
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Payment for {payment.Booking?.BookingReferenceNo} approved.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id)
        {
            var payment = await _db.Payments.Include(p => p.Booking).FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            payment.PaymentStatus = PaymentStatus.Rejected;
            payment.ConfirmedByUserId = user?.Id;
            payment.ConfirmedDate = DateTime.UtcNow;
            if (payment.Booking != null && payment.Booking.BookingStatus == BookingStatus.ForApproval)
            {
                payment.Booking.BookingStatus = BookingStatus.Pending;
            }
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Payment for {payment.Booking?.BookingReferenceNo} rejected.";
            return RedirectToAction(nameof(Index));
        }
    }
}
