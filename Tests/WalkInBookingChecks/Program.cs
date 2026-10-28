using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CourtBookingSystem.Areas.Staff.Controllers;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using CourtBookingSystem.Services.PayMongo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Real MVC model binding, validation, antiforgery, filters, Razor rendering, and
// database transactions against a disposable SQLite file. Never uses project data.
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "CourtBookingSystemFootballV2.csproj")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Run from this repository.");
var useSqlServer = args.Contains("--sqlserver");
var sqlDatabase = $"WalkInChecks_{Guid.NewGuid():N}";
var database = Path.Combine(Path.GetTempPath(), $"walkin-checks-{Guid.NewGuid():N}.db");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root });
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<CommitFailureInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
{
    if (useSqlServer) options.UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={sqlDatabase};Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5", sql => sql.CommandTimeout(5));
    else options.UseSqlite($"Data Source={database};Pooling=False;Default Timeout=5");
    options.AddInterceptors(services.GetRequiredService<CommitFailureInterceptor>());
});
builder.Services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(WalkInController).Assembly);
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
    context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, "test-staff"), new Claim(ClaimTypes.Name, "Test Staff"), new Claim(ClaimTypes.Role, "Staff")
    }, IdentityConstants.ApplicationScheme));
    await next();
});
app.UseAuthorization();
app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
var date = PhilippineTime.Today.AddDays(14);
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
async Task<int> Count()
{
    var result = 0;
    await WithDb(async db => result = await db.Bookings.CountAsync());
    return result;
}
try
{
    await WithDb(async db =>
    {
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "test-staff", UserName = "staff@test.local", FullName = "Test Staff" });
        db.CourtGroups.Add(new CourtGroup { Id = 1, GroupCode = "GROUP", GroupName = "Shared field" });
        db.Courts.AddRange(
            new Court { Id = 1, CourtName = "Split A", CourtGroupId = 1, HourlyRate = 1000, SportType = SportType.Football },
            new Court { Id = 2, CourtName = "Full field", CourtGroupId = 1, IsFullCourt = true, HourlyRate = 2500, SportType = SportType.Football },
            new Court { Id = 3, CourtName = "Split B", CourtGroupId = 1, HourlyRate = 1000, SportType = SportType.Football },
            new Court { Id = 4, CourtName = "Independent", HourlyRate = 777, SportType = SportType.Football },
            new Court { Id = 5, CourtName = "Closed", HourlyRate = 1000, Status = CourtStatus.Closed });
        foreach (var weekday in Enum.GetValues<DayOfWeek>())
            db.BusinessHours.Add(new BusinessHour { DayOfWeek = weekday, OpenTime = new(6, 0, 0), CloseTime = new(4, 0, 0) });
        db.BookingPricingRules.Add(new BookingPricingRule { CourtId = 1, Name = "Promo", IsPromotional = true,
            StartTime = new(10, 0, 0), EndTime = new(12, 0, 0), HourlyRate = 800,
            EffectiveStartDate = date, EffectiveEndDate = date.AddDays(10) });
        await db.SaveChangesAsync();
    });
    await app.StartAsync();
    var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(url) };
    var html = await client.GetStringAsync("/Staff/WalkIn/Create");
    string Input(string name) => WebUtility.HtmlDecode(Regex.Match(html, $"<input[^>]*name=\"{name}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    var token = Input("__RequestVerificationToken");
    Check(token.Length > 0 && Guid.TryParse(Input("SubmissionId"), out _), "Create page renders antiforgery and submission tokens");
    Check(html.Contains("Add to Booking") && html.Contains("selectedBookings") && html.Contains("staff-walkin.js"), "Existing Razor page renders batch controls and summary");

    Dictionary<string, string> Form(params (int Court, DateTime Date, int Hour, int Duration)[] slots)
    {
        var fields = new Dictionary<string, string> {
            ["__RequestVerificationToken"] = token, ["SubmissionId"] = Guid.NewGuid().ToString(), ["UseSlotSelection"] = "true",
            ["CourtId"] = "1", ["BookingDate"] = date.ToString("yyyy-MM-dd"), ["DurationHours"] = "1", ["StartTime"] = "00:00:00",
            ["CustomerName"] = "Walk-in Test", ["CustomerMobile"] = "09123456789", ["PaymentMethod"] = "1"
        };
        for (var i = 0; i < slots.Length; i++)
        {
            fields[$"Slots[{i}].CourtId"] = slots[i].Court.ToString();
            fields[$"Slots[{i}].BookingDate"] = slots[i].Date.ToString("yyyy-MM-dd");
            fields[$"Slots[{i}].StartTime"] = $"{slots[i].Hour:D2}:00:00";
            fields[$"Slots[{i}].DurationHours"] = slots[i].Duration.ToString();
        }
        return fields;
    }
    async Task<HttpResponseMessage> Post(Dictionary<string, string> fields, string path = "/Staff/WalkIn/Create") =>
        await client.PostAsync(path, new FormUrlEncodedContent(fields));
    async Task Reject(Dictionary<string, string> form, string reason, string description)
    {
        var before = await Count();
        using var response = await Post(form);
        var body = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Check(response.StatusCode == HttpStatusCode.OK && body.Contains(reason, StringComparison.OrdinalIgnoreCase) && await Count() == before,
            description + $" (status {(int)response.StatusCode})");
        Check(body.Contains("walkInSelectionData") && body.Contains("Walk-in Test"), description + ": selection and customer data remain on error");
    }

    using var single = await Post(Form((1, date, 8, 1)));
    Check(single.StatusCode == HttpStatusCode.Redirect && single.Headers.Location!.ToString().Contains("Today"), "One-slot Cash booking keeps existing redirect");
    await WithDb(async db =>
    {
        var booking = await db.Bookings.Include(b => b.Payments).SingleAsync();
        var payment = booking.Payments.Single();
        Check(booking.BookingStatus == BookingStatus.Confirmed && payment.PaymentStatus == PaymentStatus.Approved
            && payment.Amount == 1000 && payment.ConfirmedByUserId == "test-staff" && payment.PaidDate != null,
            "Cash keeps Confirmed/Approved status, price, and staff attribution");
        Check(booking.CustomerEmail.EndsWith("@giuseppefootball.local") && booking.BookingReferenceNo.Length <= 30, "Placeholder email and unique GFC reference remain valid");
    });

    var multiForm = Form((1, date, 10, 1), (1, date, 14, 2), (1, date.AddDays(1), 18, 1), (1, date.AddDays(2), 23, 2));
    using var multi = await Post(multiForm);
    Check(multi.StatusCode == HttpStatusCode.Redirect && await Count() == 5, "Non-consecutive slots, mixed durations, and three dates save together");
    var results = await client.GetStringAsync(multi.Headers.Location);
    Check(results.Contains("View booking") && results.Contains("FullPayment") == false && results.Contains("5,800.00"), "Batch result renders every booking with details links and configured total");
    await WithDb(async db =>
    {
        var overnight = await db.Bookings.SingleAsync(b => b.StartTime == new TimeSpan(23, 0, 0));
        Check(overnight.BookingDate == date.AddDays(2) && overnight.EndTime == new TimeSpan(1, 0, 0), "Cross-midnight record retains operating date and next-day end");
        Check(await db.Bookings.Where(b => b.StartTime == new TimeSpan(10, 0, 0)).Select(b => b.TotalAmount).SingleAsync() == 800,
            "Promotional pricing is computed on the server");
    });
    using var replay = await Post(multiForm);
    Check(replay.StatusCode == HttpStatusCode.Redirect && await Count() == 5, "Repeated POST redirects to existing batch without duplicates");
    await WithDb(async db => { var b = await db.Bookings.SingleAsync(b => b.StartTime == new TimeSpan(10, 0, 0)); b.BookingStatus = BookingStatus.Cancelled; await db.SaveChangesAsync(); });
    using var cancelledReplay = await Post(multiForm);
    Check(cancelledReplay.StatusCode == HttpStatusCode.Redirect && await Count() == 5, "Submission token remains idempotent after cancellation");

    await Reject(Form(), "Add at least one", "Empty selection rejected");
    await Reject(Form((1, date, 9, 1), (1, date, 9, 1)), "Duplicate or overlapping", "Duplicate slot rejected atomically");
    await Reject(Form((1, date, 16, 2), (1, date, 17, 1)), "Duplicate or overlapping", "Overlapping durations rejected atomically");
    await Reject(Form((1, date, 20, 1), (2, date, 20, 1)), "shared field", "Full/split overlap inside batch rejected");
    await Reject(Form((2, date, 8, 1)), "shared field", "Existing split booking blocks full field");
    await Reject(Form((1, date, 8, 1), (1, date.AddDays(4), 8, 1)), "Booked", "Another user taking one slot rejects the whole batch");
    await Reject(Form((1, PhilippineTime.Today.AddDays(-1), 8, 1)), "Past", "Past slot rejected");
    await Reject(Form((5, date, 8, 1)), "no longer available", "Closed field rejected");
    await Reject(Form((1, date, 3, 2)), "outside", "Duration extending beyond overnight closing rejected");
    await Reject(Form((1, date.AddDays(2), 0, 1)), "Booked", "Midnight start conflicts with existing cross-midnight booking");
    await WithDb(async db => { db.BlockedSlots.Add(new BlockedSlot { CourtId = 1, BlockedDate = date, StartTime = new(21, 0, 0), EndTime = new(22, 0, 0), Reason = "Maintenance" }); await db.SaveChangesAsync(); });
    await Reject(Form((1, date, 21, 1)), "Blocked", "Blocked slot rejected");
    var invalidDuration = Form((1, date, 9, 0));
    await Reject(invalidDuration, "between 1 and 8", "Nested duration validation runs through MVC");
    var missingTime = Form((1, date, 9, 1)); missingTime.Remove("Slots[0].StartTime");
    await Reject(missingTime, "required", "Missing nested start time is rejected");
    var invalidPayment = Form((1, date, 9, 1)); invalidPayment["PaymentMethod"] = "3";
    await Reject(invalidPayment, "Cash or Manual GCash", "Unsupported walk-in payment method rejected");

    var gcash = Form((1, date.AddDays(3), 10, 2), (1, date.AddDays(3), 0, 1));
    gcash["PaymentMethod"] = ((int)PaymentMethod.GCash).ToString();
    using var gcashResult = await Post(gcash);
    Check(gcashResult.StatusCode == HttpStatusCode.Redirect, "GCash batch and exact midnight start succeed");
    var gcashPage = await client.GetStringAsync(gcashResult.Headers.Location);
    Check(gcashPage.Contains("upload its GCash receipt"), "GCash batch result explains per-booking receipt handling");
    await WithDb(async db =>
    {
        var bookings = await db.Bookings.Include(b => b.Payments).Where(b => b.BookingDate == date.AddDays(3)).ToListAsync();
        Check(bookings.Count == 2 && bookings.All(b => b.BookingStatus == BookingStatus.Pending && b.Payments.Single().PaymentStatus == PaymentStatus.Unpaid
            && b.Payments.Single().ConfirmedByUserId == null), "GCash preserves Pending/Unpaid status and separate payments");
    });
    var mixed = Form((1, date.AddDays(4), 11, 2));
    using var mixedResult = await Post(mixed);
    await WithDb(async db => Check(await db.Bookings.Where(b => b.BookingDate == date.AddDays(4)).Select(b => b.TotalAmount).SingleAsync() == 1800,
        "Mixed promo/regular pricing preserves segment calculations"));
    using var splitTogether = await Post(Form((1, date.AddDays(5), 8, 1), (3, date.AddDays(5), 8, 1)));
    Check(splitTogether.StatusCode == HttpStatusCode.Redirect, "Separate split fields can be booked simultaneously");

    var legacy = Form(); legacy["UseSlotSelection"] = "false"; legacy["BookingDate"] = date.AddDays(6).ToString("yyyy-MM-dd"); legacy["StartTime"] = "09:00:00";
    using var legacyResult = await Post(legacy);
    Check(legacyResult.StatusCode == HttpStatusCode.Redirect, "Legacy single-slot POST remains supported");

    await WithDb(async db => await db.Database.ExecuteSqlRawAsync(useSqlServer
        ? "CREATE TRIGGER FailPayment ON Payments AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE Amount = 777) THROW 51000, 'Injected payment failure', 1; END"
        : "CREATE TRIGGER FailPayment BEFORE INSERT ON Payments WHEN NEW.Amount = 777 BEGIN SELECT RAISE(ABORT, 'Injected payment failure'); END;"));
    await Reject(Form((1, date.AddDays(7), 8, 1), (4, date.AddDays(7), 8, 1)), "No bookings were created", "Database payment failure rolls back all bookings and payments");
    await WithDb(async db => {
        Check(!await db.Payments.AnyAsync(p => p.Booking!.BookingDate == date.AddDays(7)), "Rollback leaves no orphaned payments");
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER FailPayment;");
    });

    var beforeCommitFailure = await Count();
    using var failedCommitRequest = new HttpRequestMessage(HttpMethod.Post, "/Staff/WalkIn/Create")
    {
        Content = new FormUrlEncodedContent(Form((1, date.AddDays(10), 8, 1), (1, date.AddDays(10), 10, 1)))
    };
    failedCommitRequest.Headers.Add("X-Test-Fail-Commit", "true");
    using var failedCommit = await client.SendAsync(failedCommitRequest);
    Check(failedCommit.StatusCode == HttpStatusCode.OK && (await failedCommit.Content.ReadAsStringAsync()).Contains("No bookings were created")
        && await Count() == beforeCommitFailure, "Commit failure rolls back the batch and replaces the success redirect with an error");

    var racing = new[] { Form((1, date.AddDays(8), 8, 1)), Form((1, date.AddDays(8), 8, 1)) };
    var beforeRace = await Count();
    var raceResults = await Task.WhenAll(racing.Select(fields => Post(fields)));
    Check(raceResults.Count(r => r.StatusCode == HttpStatusCode.Redirect) == 1 && await Count() == beforeRace + 1, "Concurrent staff submissions cannot double-book");
    foreach (var response in raceResults) response.Dispose();
    var duplicate = Form((1, date.AddDays(8), 10, 1));
    var duplicateResults = await Task.WhenAll(Post(duplicate), Post(duplicate));
    Check(duplicateResults.Any(r => r.StatusCode == HttpStatusCode.Redirect) && duplicateResults.All(r => r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK) && await Count() == beforeRace + 2, "Concurrent repeated clicks create only one booking");
    foreach (var response in duplicateResults) response.Dispose();

    var guest = Form(); guest["BookingDate"] = date.AddDays(9).ToString("yyyy-MM-dd"); guest["StartTime"] = "08:00:00";
    guest["CustomerEmail"] = "guest@test.local"; guest["AcceptedTerms"] = "true";
    var customerRace = await Task.WhenAll(Post(guest, "/Bookings/Create"), Post(Form((2, date.AddDays(9), 8, 1))));
    Check(customerRace.Count(r => r.StatusCode == HttpStatusCode.Redirect) == 1 && await Count() == beforeRace + 3,
        "Concurrent customer/split and staff/full-field submissions cannot double-book");
    foreach (var response in customerRace) response.Dispose();
    Console.WriteLine($"All {checks} integration checks passed ({(useSqlServer ? "SQL Server" : "SQLite")}).");
}
finally
{
    await app.StopAsync();
    if (useSqlServer) await WithDb(db => db.Database.EnsureDeletedAsync());
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    foreach (var suffix in new[] { "", "-wal", "-shm" })
        if (File.Exists(database + suffix)) File.Delete(database + suffix);
}
