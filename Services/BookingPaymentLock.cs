using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services;

// Call inside a serializable transaction. SQL Server serializes writers for the
// booking before loading payment totals; SQLite reserves its writer at BEGIN.
internal static class BookingPaymentLock
{
    public static IQueryable<Booking> Query(ApplicationDbContext db) => db.Database.IsSqlServer()
        ? db.Bookings.FromSqlRaw("SELECT * FROM [Bookings] WITH (UPDLOCK, HOLDLOCK)")
        : db.Bookings;
}
