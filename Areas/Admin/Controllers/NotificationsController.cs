using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class NotificationsController : Controller
    {
        private readonly ApplicationDbContext _db;

        public NotificationsController(ApplicationDbContext db) => _db = db;

        public async Task<IActionResult> Index()
        {
            var items = await _db.Notifications
                .OrderByDescending(n => n.CreatedDate)
                .Take(100)
                .ToListAsync();
            return View(items);
        }

        // /Admin/Notifications/Open/5 — marks read and redirects to Link.
        [HttpGet]
        public async Task<IActionResult> Open(int id)
        {
            var notif = await _db.Notifications.FindAsync(id);
            if (notif == null) return NotFound();

            if (!notif.IsRead)
            {
                notif.IsRead = true;
                notif.ReadDate = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return !string.IsNullOrWhiteSpace(notif.Link)
                ? Redirect(notif.Link)
                : RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            var unread = await _db.Notifications.Where(n => !n.IsRead).ToListAsync();
            foreach (var n in unread)
            {
                n.IsRead = true;
                n.ReadDate = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
            TempData["Success"] = $"{unread.Count} notification(s) marked as read.";
            return RedirectToAction(nameof(Index));
        }
    }

    public static class NotificationCreator
    {
        // Backward-compatible shim — delegates to the central NotificationService.
        public static Task NotifyAdminsAsync(
            Data.ApplicationDbContext db, string message, string? link = null)
            => Services.NotificationService.NotifyAsync(db, "Admin Notification", message, link);
    }
}
