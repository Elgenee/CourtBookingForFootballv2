using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CourtBookingSystem.Controllers;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using CourtBookingSystem.Services.PayMongo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Real MVC, antiforgery, Razor, and transactions against a disposable database.
// A controllable UTC clock makes the boundary checks independent of host time zone.
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "CourtBookingSystemFootballV2.csproj")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Run from this repository.");
var database = Path.Combine(Path.GetTempPath(), $"late-night-checks-{Guid.NewGuid():N}.db");
var date = PhilippineTime.Today.AddDays(14);
var clock = new BookingClock();
clock.SetPhilippineTime(date.AddHours(20));
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root });
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddSingleton<TimeProvider>(clock);
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={database};Pooling=False"));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(BookingsController).Assembly);
builder.Services.AddScoped<BookingAvailabilityService>();
builder.Services.AddScoped<CourtGroupAvailabilityService>();
builder.Services.AddScoped<BookingPricingService>();
builder.Services.AddScoped<PayMongoQrPhExpiryService>();
builder.Services.AddScoped<SiteContentService>();
builder.Services.Configure<FootballPaymentOptions>(_ => { });
builder.Services.Configure<PayMongoOptions>(_ => { });
builder.Services.AddHttpClient<IPayMongoClient, PayMongoClient>();
await using var app = builder.Build();
app.UseRouting();
app.Use(async (context, next) =>
{
    if (context.Request.Headers["X-Test-Role"].FirstOrDefault() is string role)
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, "test-staff"), new Claim(ClaimTypes.Role, role)
        }, IdentityConstants.ApplicationScheme));
    await next();
});
app.UseAuthorization();
app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL " + description);
    checks++;
    Console.WriteLine("PASS " + description);
}
async Task WithDb(Func<ApplicationDbContext, Task> action)
{
    await using var scope = app.Services.CreateAsyncScope();
    await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
}
try
{
    var hours = new BusinessHour { OpenTime = new(7, 0, 0), CloseTime = new(6, 0, 0) };
    var midnight = OperatingHoursHelper.ToOperatingDateTime(date, hours, TimeSpan.Zero);
    Check(midnight == date.AddDays(1), "Midnight uses the next calendar day of the operating date");
    foreach (var hour in new[] { 0, 2, 4 })
    {
        var start = midnight.AddHours(hour);
        Check(CustomerBookingRules.CanBook(start, start.AddHours(-4)), $"{hour:00}:00 allows exactly four hours");
        Check(!CustomerBookingRules.CanBook(start, start.AddHours(-4).AddTicks(1)), $"{hour:00}:00 rejects one tick under four hours");
        Check(CustomerBookingRules.CanBook(start, start.AddHours(-4).AddTicks(-1)), $"{hour:00}:00 allows more than four hours");
    }
    Check(CustomerBookingRules.CanBook(midnight, date.AddHours(20)), "Example: 8 PM to next-day midnight allowed");
    Check(!CustomerBookingRules.CanBook(midnight, date.AddHours(21)), "Example: 9 PM to next-day midnight rejected");
    Check(CustomerBookingRules.CanBook(midnight.AddHours(2), date.AddHours(22)), "Example: 10 PM to next-day 2 AM allowed");
    Check(!CustomerBookingRules.CanBook(midnight.AddHours(2), date.AddHours(23)), "Example: 11 PM to next-day 2 AM rejected");
    Check(CustomerBookingRules.CanBook(midnight.AddHours(4), midnight), "Example: midnight to same-day 4 AM allowed");
    foreach (var start in new[] { midnight.AddHours(4).AddSeconds(1), midnight.AddHours(5), date.AddHours(23) })
        Check(CustomerBookingRules.CanBook(start, start.AddMinutes(-1)) && CustomerBookingRules.GetAdvanceBookingDeadline(start) == null,
            $"{start:HH:mm:ss} outside window has no advance-booking restriction");
    Check(CustomerBookingRules.CanBook(midnight.AddDays(1), date.AddHours(23)), "Full dates distinguish next night from tonight");

    await WithDb(async db =>
    {
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "test-staff", UserName = "staff@test.local", FullName = "Staff" });
        db.Courts.Add(new Court { Id = 1, CourtName = "Test Turf", SportType = SportType.Football, HourlyRate = 1000 });
        foreach (var weekday in Enum.GetValues<DayOfWeek>())
            db.BusinessHours.Add(new BusinessHour { DayOfWeek = weekday, OpenTime = hours.OpenTime, CloseTime = hours.CloseTime });
        await db.SaveChangesAsync();
    });
    await app.StartAsync();
    var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(url) };
    var html = await client.GetStringAsync($"/Bookings/Create?courtId=1&date={date:yyyy-MM-dd}&time=00:00:00");
    var token = WebUtility.HtmlDecode(Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    Check(token.Length > 0 && html.Contains("expireLateNightSlots"), "Customer page renders antiforgery and cutoff validation");
    var renderDirectory = Environment.GetEnvironmentVariable("LATE_NIGHT_RENDER_DIR");
    if (!string.IsNullOrWhiteSpace(renderDirectory))
    {
        Directory.CreateDirectory(renderDirectory);
        await File.WriteAllTextAsync(Path.Combine(renderDirectory, "Create.html"), html);
        await File.WriteAllTextAsync(Path.Combine(renderDirectory, "Find.html"), await client.GetStringAsync("/Availability/Find"));
    }
    async Task<JsonElement> Slots(bool staffBooking = false, string? role = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/Bookings/GetSlots?courtId=1&date={date:yyyy-MM-dd}&durationHours=1&staffBooking={staffBooking}");
        if (role != null) request.Headers.Add("X-Test-Role", role);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }
    JsonElement Slot(JsonElement payload, int hour) => payload.GetProperty("slots").EnumerateArray()
        .Single(s => s.GetProperty("startTime").GetString() == $"{hour:00}:00:00");
    var atBoundary = await Slots();
    Check(Slot(atBoundary, 0).GetProperty("isAvailable").GetBoolean(), "Slot endpoint allows midnight at the exact four-hour boundary");
    Check(Slot(atBoundary, 0).GetProperty("startDate").GetString() == midnight.ToString("yyyy-MM-dd")
        && Slot(atBoundary, 0).GetProperty("advanceBookingDeadlineUtc").GetDateTime() == PhilippineTime.ToUtc(date.AddHours(20)),
        "Customer API supplies actual next-day date and UTC cutoff");
    Check(atBoundary.GetProperty("serverNowUtc").GetDateTime() == PhilippineTime.ToUtc(date.AddHours(20)), "API clock uses UTC converted to Manila, independent of server local time");
    clock.SetPhilippineTime(date.AddHours(21));
    var restricted = await Slots();
    Check(!Slot(restricted, 0).GetProperty("isAvailable").GetBoolean()
        && Slot(restricted, 0).GetProperty("reason").GetString() == CustomerBookingRules.AdvanceBookingReason,
        "Customer grid disables restricted slots with the requested explanation");
    Check(Slot(restricted, 1).GetProperty("isAvailable").GetBoolean() && Slot(restricted, 23).GetProperty("isAvailable").GetBoolean(),
        "Grid keeps exact-boundary 1 AM and unrestricted 11 PM available");
    var search = await client.GetFromJsonAsync<JsonElement>($"/Availability/Search?sport=Football&date={date:yyyy-MM-dd}&durationHours=1");
    var searchSlots = search.GetProperty("slots").EnumerateArray().ToArray();
    Check(search.GetProperty("count").GetInt32() == searchSlots.Length && searchSlots.Length == 8
        && !searchSlots.Any(s => s.GetProperty("startTime").GetString() == "00:00:00"),
        "Availability search hides restricted midnight and reports the correct count");
    Check(!Slot(await Slots(true), 0).GetProperty("isAvailable").GetBoolean(), "Guest cannot bypass customer filtering using staffBooking=true");
    foreach (var role in new[] { "Staff", "Admin" })
    {
        var staffSlots = await Slots(true, role);
        Check(Slot(staffSlots, 0).GetProperty("isAvailable").GetBoolean()
            && Slot(staffSlots, 0).GetProperty("advanceBookingDeadlineUtc").ValueKind == JsonValueKind.Null,
            role + " slot lookup preserves existing unrestricted behavior");
        Check(!Slot(await Slots(false, role), 0).GetProperty("isAvailable").GetBoolean(), role + " using the customer flow still receives customer validation");
    }
    await WithDb(async db =>
    {
        var availability = new BookingAvailabilityService(db, new CourtGroupAvailabilityService(db), new BookingPricingService(db), clock);
        var staffSlots = await availability.GenerateSlotsForCourtAsync(1, date, 1);
        Check(staffSlots.Single(s => s.Start == TimeSpan.Zero).IsAvailable, "Existing staff/calendar service calls retain their original rules");
    });
    Dictionary<string, string> Form(int hour) => new()
    {
        ["__RequestVerificationToken"] = token, ["CourtId"] = "1", ["BookingDate"] = date.ToString("yyyy-MM-dd"),
        ["StartTime"] = $"{hour:00}:00:00", ["DurationHours"] = "1", ["CustomerName"] = "Customer Test",
        ["CustomerEmail"] = "customer@test.local", ["CustomerMobile"] = "09123456789", ["PaymentMethod"] = "1", ["AcceptedTerms"] = "true",
        // Untrusted extra fields must never turn off POST validation.
        ["staffBooking"] = "true", ["customerBooking"] = "false"
    };
    async Task Reject(int hour, string description, string? role = null)
    {
        var fields = Form(hour);
        if (role != null)
        {
            using var pageRequest = new HttpRequestMessage(HttpMethod.Get, "/Bookings/Create");
            pageRequest.Headers.Add("X-Test-Role", role);
            using var page = await client.SendAsync(pageRequest);
            fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(await page.Content.ReadAsStringAsync(),
                "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Bookings/Create") { Content = new FormUrlEncodedContent(fields) };
        if (role != null) request.Headers.Add("X-Test-Role", role);
        using var response = await client.SendAsync(request);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Check(response.StatusCode == HttpStatusCode.OK && body.Contains(CustomerBookingRules.AdvanceBookingMessage), description);
        await WithDb(async db => Check(!await db.Bookings.AnyAsync() && !await db.Payments.AnyAsync(), description + ": no booking or payment written"));
    }
    await Reject(0, "Stale page/direct POST cannot book midnight after its cutoff");
    await Reject(0, "Staff role cannot bypass validation on a customer POST", "Staff");
    foreach (var (now, hour) in new[] { (date.AddHours(23), 2), (midnight.AddTicks(1), 4) })
    {
        clock.SetPhilippineTime(now);
        await Reject(hour, $"Direct POST rejects {hour:00}:00 under four hours, including after midnight");
    }
    async Task Accept(DateTime now, int hour, string description)
    {
        clock.SetPhilippineTime(now);
        using var response = await client.PostAsync("/Bookings/Create", new FormUrlEncodedContent(Form(hour)));
        Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.ToString().Contains("Confirmation"), description);
        await WithDb(async db =>
        {
            var booking = await db.Bookings.Include(b => b.Payments).SingleAsync();
            Check(booking.BookingDate == date && booking.StartTime == TimeSpan.FromHours(hour) && booking.TotalAmount == 1000
                && booking.BookingStatus == BookingStatus.Pending && booking.Payments.Single().PaymentStatus == PaymentStatus.Unpaid,
                description + ": operating date, price, payment, and status preserved");
            db.Payments.RemoveRange(booking.Payments);
            db.Bookings.Remove(booking);
            await db.SaveChangesAsync();
        });
    }
    await Accept(date.AddHours(20), 0, "Customer POST allows exactly four hours before next-day midnight");
    await Accept(date.AddHours(22), 2, "Customer POST allows exactly four hours before next-day 2 AM");
    await Accept(midnight, 4, "Customer POST allows 4 AM exactly four hours after midnight");
    await Accept(date.AddHours(22).AddMinutes(30), 23, "Customer POST outside window keeps existing short-notice booking behavior");
    // A staff walk-in during the same restricted period remains bookable end to end.
    clock.SetPhilippineTime(date.AddHours(23));
    using var staffPageRequest = new HttpRequestMessage(HttpMethod.Get, "/Staff/WalkIn/Create");
    staffPageRequest.Headers.Add("X-Test-Role", "Staff");
    using var staffPage = await client.SendAsync(staffPageRequest);
    var staffHtml = await staffPage.Content.ReadAsStringAsync();
    Check(staffHtml.Contains("staffBooking=True", StringComparison.OrdinalIgnoreCase), "Staff view explicitly requests staff slot rules");
    var staffForm = Form(0);
    staffForm["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(staffHtml,
        "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    staffForm["SubmissionId"] = Guid.NewGuid().ToString();
    staffForm["UseSlotSelection"] = "false";
    using var staffPost = new HttpRequestMessage(HttpMethod.Post, "/Staff/WalkIn/Create") { Content = new FormUrlEncodedContent(staffForm) };
    staffPost.Headers.Add("X-Test-Role", "Staff");
    using var staffResponse = await client.SendAsync(staffPost);
    Check(staffResponse.StatusCode == HttpStatusCode.Redirect, "Staff can still create a midnight walk-in less than four hours ahead");
    Console.WriteLine($"All {checks} late-night booking checks passed.");
}
finally
{
    await app.StopAsync();
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    foreach (var suffix in new[] { "", "-wal", "-shm" })
        if (File.Exists(database + suffix)) File.Delete(database + suffix);
}

sealed class BookingClock : TimeProvider
{
    private DateTimeOffset _utcNow;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public void SetPhilippineTime(DateTime value) => _utcNow = new DateTimeOffset(PhilippineTime.ToUtc(value));
}
