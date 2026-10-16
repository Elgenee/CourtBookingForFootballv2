using System.Data.Common;
using System.Reflection;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using CourtBookingSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Dependency-free regression checks; uses only a disposable in-memory database.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
var date = PhilippineTime.Today.AddDays(14);
var historicalDate = PhilippineTime.Today.AddDays(-14);
var closedDate = date.AddDays(1);
var noHoursDate = date.AddDays(2);
var shortDay = date.AddDays(3);
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
    Console.WriteLine($"PASS {message}");
}

await using (var seed = new ApplicationDbContext(options))
{
    await seed.Database.EnsureCreatedAsync();
    foreach (var weekday in Enum.GetValues<DayOfWeek>())
    {
        if (weekday == noHoursDate.DayOfWeek) continue;
        seed.BusinessHours.Add(new()
        {
            DayOfWeek = weekday,
            OpenTime = weekday == shortDay.DayOfWeek ? new(6, 30, 0) : new(6, 0, 0),
            CloseTime = weekday == shortDay.DayOfWeek ? new(8, 0, 0) : new(4, 0, 0),
            IsClosed = weekday == closedDate.DayOfWeek
        });
    }
    seed.CourtGroups.Add(new() { Id = 1, GroupName = "Football field", GroupCode = "FOOTBALL" });
    seed.Courts.AddRange(
        new Court { Id = 1, CourtName = "A split", CourtGroupId = 1, HourlyRate = 1800 },
        new Court { Id = 2, CourtName = "Full field", CourtGroupId = 1, IsFullCourt = true, HourlyRate = 3600 },
        new Court { Id = 3, CourtName = "B split", CourtGroupId = 1, HourlyRate = 1800 },
        new Court { Id = 4, CourtName = "Independent", HourlyRate = 1200 },
        new Court { Id = 5, CourtName = "Maintenance", Status = CourtStatus.UnderMaintenance },
        new Court { Id = 6, CourtName = "Inactive", IsActive = false },
        new Court { Id = 7, CourtName = "Closed", Status = CourtStatus.Closed });
    seed.BookingPricingRules.Add(new()
    {
        CourtId = 1, Name = "Evening", StartTime = new(18, 0, 0), EndTime = new(4, 0, 0), HourlyRate = 2800
    });
    Booking Reservation(string name, DateTime operatingDate, int court, TimeSpan start, TimeSpan end, BookingStatus status) => new()
    {
        BookingReferenceNo = Guid.NewGuid().ToString("N")[..20],
        CustomerName = name,
        CustomerEmail = "test@example.com",
        CustomerMobile = "09123456789",
        CourtId = court, BookingDate = operatingDate, StartTime = start, EndTime = end, BookingStatus = status
    };
    seed.Bookings.AddRange(
        Reservation("Multi hour", date, 1, new(9, 0, 0), new(12, 0, 0), BookingStatus.Confirmed),
        Reservation("Cancelled", date, 1, new(12, 0, 0), new(13, 0, 0), BookingStatus.Cancelled),
        Reservation("Full field customer", date, 2, new(14, 0, 0), new(15, 0, 0), BookingStatus.Pending),
        Reservation("Other split customer", date, 3, new(16, 0, 0), new(17, 0, 0), BookingStatus.ForApproval),
        Reservation("Partial hour", date, 1, new(17, 30, 0), new(18, 30, 0), BookingStatus.PartiallyPaid),
        Reservation("Shared hour", date, 1, new(20, 0, 0), new(20, 30, 0), BookingStatus.Confirmed),
        Reservation("Shared hour", date, 1, new(20, 30, 0), new(21, 0, 0), BookingStatus.Confirmed),
        Reservation("Overnight team", date, 1, new(23, 0, 0), new(2, 0, 0), BookingStatus.Confirmed),
        Reservation("After midnight", date, 1, new(2, 0, 0), new(3, 0, 0), BookingStatus.Pending),
        Reservation("Past customer", historicalDate, 1, new(9, 0, 0), new(10, 0, 0), BookingStatus.Confirmed),
        Reservation("Completed customer", historicalDate, 1, new(10, 0, 0), new(11, 0, 0), BookingStatus.Completed),
        Reservation("Historical shared field", historicalDate, 2, new(14, 0, 0), new(15, 0, 0), BookingStatus.Confirmed));
    seed.BlockedSlots.AddRange(
        new BlockedSlot { CourtId = 1, BlockedDate = date, StartTime = new(13, 0, 0), EndTime = new(14, 0, 0), Reason = "Maintenance" },
        new BlockedSlot { CourtId = 1, BlockedDate = date, StartTime = new(3, 0, 0), EndTime = new(4, 0, 0), Reason = "Overnight block" },
        new BlockedSlot { CourtId = 1, BlockedDate = historicalDate, StartTime = new(13, 0, 0), EndTime = new(14, 0, 0), Reason = "Past block" });
    await seed.SaveChangesAsync();
}

// Any INSERT/UPDATE/DELETE, including an on-read status transition, fails the checks.
var guarded = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection)
    .AddInterceptors(new SelectOnlyInterceptor()).Options;
await using var db = new ApplicationDbContext(guarded);
var groups = new CourtGroupAvailabilityService(db);
var pricing = new BookingPricingService(db);
var availability = new BookingAvailabilityService(db, groups, pricing);
var calendar = new DailyBookingCalendarService(db, availability, groups, pricing);
DailyCalendarSlot Slot(DailyBookingCalendarViewModel model, int hour) => model.Slots.Single(s => s.Start.Hour == hour);

var day = await calendar.BuildAsync(date, 1);
Check(day.Slots.Count == 22, "6 AM–4 AM operating day contains 22 hourly slots");
Check(day.Slots.Where(s => s.Start.Hour is 9 or 10 or 11).All(s => s.Status == "booked" && s.Label == "Multi hour"), "All hours of a multi-hour reservation are booked");
var multiHourId = await db.Bookings.Where(b => b.CustomerName == "Multi hour").Select(b => b.Id).SingleAsync();
Check(day.Slots.Where(s => s.Start.Hour is 9 or 10 or 11).All(s => s.Bookings.Single().Id == multiHourId), "Every hour of a booking links to the same booking ID");
Check(day.Slots.Where(s => s.Status != "booked").All(s => s.Bookings.Count == 0), "Available, blocked, and unavailable slots do not link to unrelated bookings");
Check(Slot(day, 20).Bookings.Count == 2 && Slot(day, 20).Bookings.Select(b => b.Id).Distinct().Count() == 2, "Adjacent partial-hour reservations retain separate booking links even with identical names");
Check(Slot(day, 8).Status == "available" && Slot(day, 12).Status == "available", "Touching endpoints do not overlap; cancelled bookings do not reserve slots");
Check(Slot(day, 13).Status == "blocked" && Slot(day, 13).Description == "Maintenance", "Manual blocks retain their reason");
Check(Slot(day, 14).Status == "unavailable" && Slot(day, 14).Description!.Contains("Full field"), "Full-field booking blocks a split field");
Check(Slot(day, 16).Status == "available", "Another split-field booking does not block this split field");
Check(Slot(day, 17).Status == "booked" && Slot(day, 18).Status == "booked", "Partial-hour booking marks every affected hourly slot");
Check(Slot(day, 23).Label == "Overnight team" && Slot(day, 0).Label == "Overnight team" && Slot(day, 1).Label == "Overnight team", "Cross-midnight booking marks every affected hour");
Check(Slot(day, 23).Bookings.Single().Id == Slot(day, 0).Bookings.Single().Id
    && Slot(day, 0).Bookings.Single().Id == Slot(day, 1).Bookings.Single().Id, "Overnight slots link to the correct cross-midnight reservation");
Check(Slot(day, 0).Start.Date == date.AddDays(1) && Slot(day, 2).Label == "After midnight", "After-midnight reservations belong to the next calendar date of this operating day");
Check(Slot(day, 3).Status == "blocked", "After-midnight blocks use the operating date");
Check(Slot(day, 7).HourlyRate == 1800 && Slot(day, 19).HourlyRate == 2800, "Available slots use existing pricing rules");
Check(day.Days.Count % 7 == 0 && day.Days.First().Date.DayOfWeek == DayOfWeek.Sunday && day.Days.Last().Date.DayOfWeek == DayOfWeek.Saturday, "Month grid starts Sunday and ends Saturday");
var full = await calendar.BuildAsync(date, 2);
Check(Slot(full, 16).Status == "unavailable", "Split-field booking blocks the full field");
var independent = await calendar.BuildAsync(date, 4);
Check(independent.Slots.All(s => s.Status == "available"), "Independent field is unaffected by other field bookings");
foreach (var id in new[] { 5, 6, 7 })
    Check((await calendar.BuildAsync(date, id)).Slots.All(s => s.Status == "unavailable"), $"Field {id} is unavailable when inactive, closed, or under maintenance");
var history = await calendar.BuildAsync(historicalDate, 1);
Check(Slot(history, 9).Label == "Past customer" && Slot(history, 9).Status == "booked", "Historical reservations remain visible despite public Past reason");
Check(Slot(history, 10).Label == "Completed customer", "Completed historical reservations remain visible");
Check(Slot(history, 13).Status == "blocked" && Slot(history, 14).Status == "unavailable", "Historical manual blocks and shared-field conflicts remain visible");
Check(Slot(history, 8).Label == "Past" && history.AvailableHours == 0, "Past free hours are unavailable");
Check((await calendar.BuildAsync(closedDate, 1)).Slots.Count == 0, "Closed operating day has no bookable slots");
Check((await calendar.BuildAsync(noHoursDate, 1)).Slots.Count == 0, "Missing operating-hours configuration is treated as closed");
Check((await calendar.BuildAsync(shortDay, 1)).Slots.Single().Start.TimeOfDay == new TimeSpan(6, 30, 0), "Non-hour-aligned opening and incomplete final hour match existing availability logic");
try
{
    await calendar.BuildAsync(date, -1);
    throw new InvalidOperationException("Unknown field accepted");
}
catch (ArgumentException) { Check(true, "Unknown field is rejected"); }
Check(!db.ChangeTracker.HasChanges(), "Calendar reads do not change tracked data");
Check((await db.Bookings.AsNoTracking().SingleAsync(b => b.CustomerName == "Past customer")).BookingStatus == BookingStatus.Confirmed, "Historical view does not auto-complete reservations");
foreach (var type in new[] { typeof(CourtBookingSystem.Areas.Admin.Controllers.BookingsController), typeof(CourtBookingSystem.Areas.Staff.Controllers.BookingsController) })
{
    var action = type.GetMethod("DailyCalendar")!;
    Check(type.GetCustomAttribute<AuthorizeAttribute>()?.Roles?.Contains("Admin") == true
        && action.GetCustomAttribute<HttpGetAttribute>() != null
        && action.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore == true,
        $"{type.Namespace} daily endpoint requires role authorization, GET, and no-store caching");
}
Console.WriteLine($"{checks} daily calendar checks passed; no writes allowed during calendar reads.");

sealed class SelectOnlyInterceptor : DbCommandInterceptor
{
    private static void Validate(DbCommand command)
    {
        if (!command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Calendar attempted a database write: " + command.CommandText);
    }
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    { Validate(command); return result; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    { Validate(command); return ValueTask.FromResult(result); }
    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    { Validate(command); return result; }
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { Validate(command); return ValueTask.FromResult(result); }
}
