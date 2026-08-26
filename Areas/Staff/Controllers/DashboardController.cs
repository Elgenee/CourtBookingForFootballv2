using CourtBookingSystem.Areas.Staff.ViewModels;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.Staff.Controllers
{
    [Area("Staff")]
    [Authorize(Roles = "Staff,Admin")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _db;

        public DashboardController(ApplicationDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            // Auto-promote past Confirmed bookings to Completed.
            await CourtBookingSystem.Services.BookingStatusHelper.AutoCompletePastBookingsAsync(_db);

            var today = PhilippineTime.Today;
            var todays = await _db.Bookings
                .Include(b => b.Court)
                .Include(b => b.Payments)
                .Where(b => b.BookingDate.Date == today)
                .ToListAsync();

            var revenueToday = todays
                .Where(b => b.BookingStatus != BookingStatus.Cancelled)
                .SelectMany(b => b.Payments)
                .Where(p => p.PaymentStatus == PaymentStatus.Approved)
                .Sum(p => p.Amount);

            var vm = new StaffDashboardViewModel
            {
                TodayBookingsCount = todays.Count(b => b.BookingStatus != BookingStatus.Cancelled),
                PendingApprovalCount = todays.Count(b =>
                    b.BookingStatus == BookingStatus.Pending
                    || b.BookingStatus == BookingStatus.ForApproval),
                ConfirmedTodayCount = todays.Count(b => b.BookingStatus == BookingStatus.Confirmed),
                RevenueToday = revenueToday,
                WalkInsToday = todays.Count(b =>
                    b.CustomerEmail.EndsWith("@giuseppefootball.local", StringComparison.OrdinalIgnoreCase)),
                UpcomingToday = todays
                    .Where(b => b.BookingStatus == BookingStatus.Confirmed
                             || b.BookingStatus == BookingStatus.Pending
                             || b.BookingStatus == BookingStatus.ForApproval)
                    .OrderBy(b => b.StartTime)
                    .Take(10)
                    .ToList()
            };
            return View(vm);
        }
    }
}
