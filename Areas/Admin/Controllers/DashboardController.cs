using CourtBookingSystem.Areas.Admin.ViewModels;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _db;

        public DashboardController(ApplicationDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            // Reflect "Booking date passes → Completed" before reading stats.
            await Services.BookingStatusHelper.AutoCompletePastBookingsAsync(_db);

            var today = PhilippineTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);
            var nextMonthStart = monthStart.AddMonths(1);
            var monthStartUtc = PhilippineTime.ToUtc(monthStart);
            var nextMonthStartUtc = PhilippineTime.ToUtc(nextMonthStart);

            var activeCourts = await _db.Courts.CountAsync(c => c.IsActive);

            var bookingsToday = await _db.Bookings
                .CountAsync(b => b.BookingDate.Date == today
                    && b.BookingStatus != BookingStatus.Cancelled);

            // "Pending Payments" widget = anything awaiting customer action or
            // staff review (Unpaid + Submitted).
            var pendingPayments = await _db.Payments
                .CountAsync(p => p.PaymentStatus == PaymentStatus.Submitted
                              || p.PaymentStatus == PaymentStatus.Unpaid);

            var revenuePayments = await _db.Payments
                .Where(p => p.PaymentStatus == PaymentStatus.Approved
                            && p.ConfirmedDate != null
                            && p.ConfirmedDate >= monthStartUtc
                            && p.ConfirmedDate < nextMonthStartUtc)
                .Select(p => p.Amount)
                .ToListAsync();
            var revenueThisMonth = revenuePayments.Sum();

            var recent = await _db.Bookings
                .Include(b => b.Court)
                .OrderByDescending(b => b.CreatedDate)
                .Take(8)
                .ToListAsync();

            var bySport = await _db.Bookings
                .Include(b => b.Court)
                .Where(b => b.BookingStatus != BookingStatus.Cancelled)
                .GroupBy(b => b.Court!.SportType)
                .Select(g => new { Sport = g.Key, Count = g.Count() })
                .ToListAsync();

            var vm = new DashboardViewModel
            {
                ActiveCourts = activeCourts,
                BookingsToday = bookingsToday,
                PendingPaymentsCount = pendingPayments,
                RevenueThisMonth = revenueThisMonth,
                RecentBookings = recent,
                BookingsBySport = bySport.Select(x => new KeyValuePair<SportType, int>(x.Sport, x.Count)).ToList()
            };
            return View(vm);
        }
    }
}
