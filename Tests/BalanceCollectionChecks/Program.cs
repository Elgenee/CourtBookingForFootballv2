using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Exercise real routes, binding, authorization, antiforgery, Razor views,
// signed webhooks and transactions. All data lives in disposable databases.
var root = Directory.GetCurrentDirectory();
while (!File.Exists(Path.Combine(root, "CourtBookingSystemFootballV2.csproj")))
    root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Run from this repository.");
var sqlServer = args.Contains("--sqlserver");
var database = Path.Combine(Path.GetTempPath(), $"balance-checks-{Guid.NewGuid():N}.db");
var sqlDatabase = $"BalanceChecks_{Guid.NewGuid():N}";
const string secret = "test-only-webhook-secret";
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root });
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.ClearProviders();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<CommitFailureInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
{
    if (sqlServer) options.UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={sqlDatabase};Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5");
    else options.UseSqlite($"Data Source={database};Pooling=False;Default Timeout=5");
    options.AddInterceptors(services.GetRequiredService<CommitFailureInterceptor>());
});
builder.Services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.AddControllersWithViews().AddApplicationPart(typeof(BookingsController).Assembly);
builder.Services.AddScoped<BookingAvailabilityService>();
builder.Services.AddScoped<CourtGroupAvailabilityService>();
builder.Services.AddScoped<BookingPricingService>();
builder.Services.AddScoped<PayMongoQrPhExpiryService>();
builder.Services.AddScoped<SiteContentService>();
builder.Services.AddScoped<CashBalanceCollectionService>();
builder.Services.Configure<FootballPaymentOptions>(_ => { });
builder.Services.Configure<PayMongoOptions>(o => { o.SecretKey = "test-only"; o.WebhookSecret = secret; o.IsSandbox = true; });
builder.Services.AddSingleton<IPayMongoClient, TestPayMongoClient>();
builder.Services.AddSingleton<IPayMongoWebhookVerifier, PayMongoWebhookVerifier>();
await using var app = builder.Build();
app.UseRouting();
app.Use(async (context, next) =>
{
    var role = context.Request.Headers["X-Test-Role"].FirstOrDefault() ?? "Staff";
    context.User = role == "Anonymous" ? new ClaimsPrincipal(new ClaimsIdentity()) : new ClaimsPrincipal(new ClaimsIdentity(new[] {
        new Claim(ClaimTypes.NameIdentifier, "test-staff"), new Claim(ClaimTypes.Name, "Test Staff"), new Claim(ClaimTypes.Role, role)
    }, IdentityConstants.ApplicationScheme));
    await next();
});
app.UseAuthorization();
app.MapControllers();
app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL " + label);
    checks++;
    Console.WriteLine("PASS " + label);
}
async Task WithDb(Func<ApplicationDbContext, Task> action)
{
    await using var scope = app.Services.CreateAsyncScope();
    await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
}
async Task<Booking> Load(int id)
{
    Booking? result = null;
    await WithDb(async db => result = await db.Bookings.Include(b => b.Payments).AsNoTracking().SingleAsync(b => b.Id == id));
    return result!;
}
async Task<int> Seed(decimal amount = 1400m, PaymentStatus paymentStatus = PaymentStatus.Unpaid,
    BookingStatus bookingStatus = BookingStatus.Pending)
{
    var id = 0;
    await WithDb(async db =>
    {
        var booking = new Booking { CourtId = 1, BookingReferenceNo = $"GFC-20261029-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            CustomerName = "Balance Customer", CustomerEmail = "guest@test.local", CustomerMobile = "09171234567",
            TotalAmount = 2800m, BookingStatus = bookingStatus, BookingDate = PhilippineTime.Today.AddDays(14), StartTime = new(10, 0, 0), EndTime = new(12, 0, 0) };
        booking.Payments.Add(new Payment { Amount = amount, PaymentMethod = PaymentMethod.PayMongoQrPh,
            PaymentPurpose = amount == 2800m ? PaymentPurpose.FullPayment : PaymentPurpose.Reservation,
            PaymentStatus = paymentStatus, CheckoutExpiresAtUtc = DateTime.UtcNow.AddMinutes(8),
            PaidDate = paymentStatus == PaymentStatus.Approved ? DateTime.UtcNow : null });
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        id = booking.Id;
    });
    return id;
}
try
{
    await WithDb(async db =>
    {
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "test-staff", UserName = "staff@test.local", FullName = "Test Staff" });
        db.Courts.Add(new Court { CourtName = "Test Turf", SportType = SportType.Football, HourlyRate = 1400 });
        await db.SaveChangesAsync();
    });
    await app.StartAsync();
    var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(url) };
    string Input(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html, $"<input[^>]*name=\"{name}\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    async Task<Dictionary<string, string>> Form(int id, string amount, string area = "Staff")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{area}/Bookings/Details/{id}");
        if (area == "Admin") request.Headers.Add("X-Test-Role", "Admin");
        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();
        Check(response.StatusCode == HttpStatusCode.OK && html.Contains("collectBalanceModal") && html.Contains("Payment Transaction History"), $"{area} details render collection controls and history");
        return new() { ["Id"] = id.ToString(), ["Amount"] = amount, ["SubmissionId"] = Input(html, "SubmissionId"), ["__RequestVerificationToken"] = Input(html, "__RequestVerificationToken") };
    }
    async Task<HttpResponseMessage> Post(Dictionary<string, string> form, string area = "Staff", string? role = null, bool failCommit = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/{area}/Bookings/CollectBalance") { Content = new FormUrlEncodedContent(form) };
        if (role != null) request.Headers.Add("X-Test-Role", role);
        if (failCommit) request.Headers.Add("X-Test-Fail-Commit", "true");
        return await client.SendAsync(request);
    }
    async Task<HttpResponseMessage> Webhook(int id, bool signed = true, string eventType = "payment.paid", bool balance = false, long? paidAt = null)
    {
        var payment = (await Load(id)).Payments.First(p => p.PaymentMethod == PaymentMethod.PayMongoQrPh && (!balance || p.PaymentPurpose == PaymentPurpose.Balance));
        var raw = JsonSerializer.Serialize(new { data = new { id = "evt_test", attributes = new {
            type = eventType, data = new { id = $"pay_test_{payment.Id}", type = "payment", attributes = new {
                paid_at = paidAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds(), metadata = new { paymentId = payment.Id.ToString() }
            } }
        } } });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/paymongo/webhook") { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
        if (signed)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{raw}"))).ToLowerInvariant();
            request.Headers.Add("Paymongo-Signature", $"t={timestamp},te={signature}");
        }
        return await client.SendAsync(request);
    }

    var full = await Seed(2800m);
    Check((await Webhook(full, false)).StatusCode == HttpStatusCode.Unauthorized && PaymentSummaryHelper.Calculate(await Load(full)).AmountPaid == 0m, "Unsigned webhook cannot mark payment paid");
    Check((await Webhook(full)).StatusCode == HttpStatusCode.OK && PaymentSummaryHelper.Calculate(await Load(full)).Status == "FullyPaid", "Verified full PayMongo payment is Fully Paid");
    Check((await Load(full)).BookingStatus == BookingStatus.Confirmed, "Full payment confirms booking");

    var staleId = await Seed();
    await WithDb(async db =>
    {
        var stalePayment = await db.Payments.SingleAsync(p => p.BookingId == staleId);
        await Webhook(staleId);
        stalePayment.PaymentStatus = PaymentStatus.Rejected;
        var conflict = false;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { conflict = true; }
        Check(conflict && (await Load(staleId)).Payments.Single().PaymentStatus == PaymentStatus.Approved,
            "Stale expiry/proof updates cannot overwrite a verified payment");
    });

    var reservation = await Seed();
    Check(PaymentSummaryHelper.Calculate(await Load(reservation)).Status == "Unpaid", "Pending PayMongo reservation contributes nothing to totals");
    await Webhook(reservation);
    var original = (await Load(reservation)).Payments.Single();
    Check(PaymentSummaryHelper.Calculate(await Load(reservation)) is { AmountPaid: 1400m, RemainingBalance: 1400m, Status: "PartiallyPaid" }
        && (await Load(reservation)).BookingStatus == BookingStatus.Confirmed, "Verified 50% reservation is Partially Paid and booking remains Confirmed");
    var form = await Form(reservation, "400.00");
    Check(form["SubmissionId"].Length > 0 && form["__RequestVerificationToken"].Length > 0, "Collection form has independent submission and antiforgery keys");
    var invalid = new Dictionary<string, string>(form); invalid.Remove("__RequestVerificationToken");
    Check((await Post(invalid)).StatusCode == HttpStatusCode.BadRequest, "Cash collection requires antiforgery token");
    Check((await Post(form, role: "Customer")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect, "Customer cannot collect cash");
    Check((await Post(form, role: "Anonymous")).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect, "Anonymous user cannot collect cash");
    Check((await Load(reservation)).Payments.Count == 1, "Unauthorized requests save no payments");
    Check((await Post(form)).StatusCode == HttpStatusCode.Redirect, "Staff records a partial cash payment");
    var partial = await Load(reservation);
    var cash = partial.Payments.Single(p => p.PaymentMethod == PaymentMethod.Cash);
    Check(PaymentSummaryHelper.Calculate(partial) is { AmountPaid: 1800m, RemainingBalance: 1000m, Status: "PartiallyPaid" }
        && partial.BookingStatus == BookingStatus.Confirmed, "Partial cash payment updates balance without changing confirmed status");
    Check(cash.ConfirmedByUserId == "test-staff" && cash.PaidDate.HasValue && cash.ConfirmedDate.HasValue
        && cash.PaymentPurpose == PaymentPurpose.Balance && cash.CollectionSubmissionId.HasValue, "Cash audit records amount, method, staff, date and unique reference");
    using (var review = await client.PostAsync($"/Staff/Payments/Reject/{cash.Id}", new FormUrlEncodedContent(new Dictionary<string, string> {
        ["__RequestVerificationToken"] = form["__RequestVerificationToken"] })))
        Check((await Load(reservation)).Payments.Single(p => p.Id == cash.Id).PaymentStatus == PaymentStatus.Approved,
            "Proof review cannot reject an approved cash collection");
    await Post(form);
    Check((await Load(reservation)).Payments.Count == 2, "Retrying the same cash form records no duplicate");
    var reused = new Dictionary<string, string>(form) { ["Amount"] = "100.00" };
    await Post(reused);
    Check((await Load(reservation)).Payments.Count == 2, "Reusing a submission key with a different amount is rejected");
    foreach (var amount in new[] { "1000.01", "0", "-1", "0.001", "not-a-number" })
    {
        var bad = new Dictionary<string, string>(form) { ["Amount"] = amount, ["SubmissionId"] = Guid.NewGuid().ToString() };
        await Post(bad);
        Check((await Load(reservation)).Payments.Count == 2, $"Invalid/excessive amount {amount} is rejected on server");
    }
    var adminForm = await Form(reservation, "1000.00", "Admin");
    await Post(adminForm, "Admin", "Admin");
    var paid = await Load(reservation);
    Check(PaymentSummaryHelper.Calculate(paid) is { AmountPaid: 2800m, RemainingBalance: 0m, Status: "FullyPaid" }, "Admin collects remainder and booking becomes Fully Paid");
    var preserved = paid.Payments.Single(p => p.Id == original.Id);
    Check(preserved.Amount == original.Amount && preserved.GatewayTransactionId == original.GatewayTransactionId
        && preserved.RawWebhookPayload == original.RawWebhookPayload && preserved.PaymentStatus == original.PaymentStatus, "Cash collection preserves original PayMongo record");
    var details = await client.GetStringAsync($"/Staff/Bookings/Details/{reservation}");
    Check(details.Contains("Fully Paid") && details.Contains("Test Staff") && details.Contains("CASH-") && !details.Contains("collect-balance-btn"), "Staff details show settled summary and audit history without collection button");
    var publicHtml = await client.GetStringAsync($"/Bookings/Confirmation?reference={paid.BookingReferenceNo}");
    Check(publicHtml.Contains("Fully Paid") && publicHtml.Contains("2800.00") && !publicHtml.Contains("collectBalance") && !publicHtml.Contains("Collect Balance"), "Customer details show settled balance without staff functionality");
    var status = await client.GetFromJsonAsync<JsonElement>($"/Bookings/ConfirmationStatus?reference={paid.BookingReferenceNo}");
    Check(status.GetProperty("balancePaymentStatus").GetString() == "FullyPaid" && status.GetProperty("remainingBalance").GetDecimal() == 0m, "Customer polling returns aggregate paid status and balance");
    var receipt = await client.GetStringAsync($"/Bookings/Receipt?reference={paid.BookingReferenceNo}");
    Check(receipt.Contains("Fully Paid"), "Customer printable receipt reflects cash settlement");
    await Webhook(reservation);
    Check((await Load(reservation)).Payments.Count == 3 && PaymentSummaryHelper.Calculate(await Load(reservation)).AmountPaid == 2800m, "Repeated PayMongo webhook remains idempotent after cash settlement");

    // The booking list must filter by the same approved-only totals as details,
    // independently of the booking lifecycle and the latest transaction status.
    var listUnpaid = await Seed();
    var listRejected = await Seed(paymentStatus: PaymentStatus.Rejected, bookingStatus: BookingStatus.Confirmed);
    var listCentBalance = await Seed(2799.99m, PaymentStatus.Approved, BookingStatus.Confirmed);
    var listCompleted = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.Completed);
    var listTransition = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.Confirmed);
    await WithDb(async db =>
    {
        db.Payments.AddRange(
            new Payment { BookingId = staleId, Amount = 1400m, PaymentMethod = PaymentMethod.PayMongoQrPh, PaymentPurpose = PaymentPurpose.Balance, PaymentStatus = PaymentStatus.Unpaid },
            new Payment { BookingId = staleId, Amount = 1400m, PaymentMethod = PaymentMethod.PayMongoQrPh, PaymentPurpose = PaymentPurpose.Balance, PaymentStatus = PaymentStatus.Rejected });
        for (var i = 0; i < 205; i++)
        {
            var limitBooking = new Booking { CourtId = 1, BookingReferenceNo = $"LIST-LIMIT-{i:D3}",
                CustomerName = "List Limit Check", CustomerEmail = "list@test.local", CustomerMobile = "09171234567",
                BookingDate = PhilippineTime.Today.AddDays(i == 204 ? 1 : 90), StartTime = new(10, 0, 0), EndTime = new(12, 0, 0),
                TotalAmount = 2800m, BookingStatus = BookingStatus.Confirmed };
            limitBooking.Payments.Add(new Payment { Amount = i == 204 ? 1400m : 2800m,
                PaymentMethod = PaymentMethod.PayMongoQrPh, PaymentStatus = PaymentStatus.Approved });
            db.Bookings.Add(limitBooking);
        }
        await db.SaveChangesAsync();
    });
    async Task<string> ListHtml(string area, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{area}/Bookings?{query}");
        if (area == "Admin") request.Headers.Add("X-Test-Role", "Admin");
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
    HashSet<int> ListIds(string html) => Regex.Matches(html, "data-testid=\"(?:booking|staff-booking)-row-(\\d+)\"")
        .Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();
    var listDate = PhilippineTime.Today.AddDays(14).ToString("yyyy-MM-dd");
    var listScope = $"FromDate={listDate}&ToDate={listDate}";
    foreach (var area in new[] { "Admin", "Staff" })
    {
        var partialHtml = await ListHtml(area, $"{listScope}&PaymentState=PartiallyPaid");
        var partialIds = ListIds(partialHtml);
        Check(partialIds.Contains(staleId) && partialIds.Contains(listCentBalance) && partialIds.Contains(listTransition)
            && !partialIds.Contains(full) && !partialIds.Contains(reservation) && !partialIds.Contains(listUnpaid) && !partialIds.Contains(listRejected),
            $"{area} partially paid filter matches confirmed reservations using approved-only totals");
        Check(partialHtml.Contains("Payment Status") && partialHtml.Contains("Booking Status") && partialHtml.Contains("Partially Paid")
            && partialHtml.Contains("0.01"), $"{area} list displays separate statuses and exact remaining balances");
        var fullIds = ListIds(await ListHtml(area, $"{listScope}&PaymentState=FullyPaid"));
        Check(fullIds.Contains(full) && fullIds.Contains(reservation) && !fullIds.Contains(staleId) && !fullIds.Contains(listCentBalance),
            $"{area} fully paid filter includes full online and mixed cash settlements");
        var unpaidIds = ListIds(await ListHtml(area, $"{listScope}&PaymentState=Unpaid"));
        Check(unpaidIds.Contains(listUnpaid) && unpaidIds.Contains(listRejected) && !unpaidIds.Contains(staleId) && !unpaidIds.Contains(full),
            $"{area} unpaid filter excludes pending and rejected online transactions from paid totals");
        var combinedIds = ListIds(await ListHtml(area, $"{listScope}&Status=3&PaymentState=PartiallyPaid"));
        Check(combinedIds.Contains(staleId) && !combinedIds.Contains(listCompleted),
            $"{area} booking status and payment status filters work together");
        var completedIds = ListIds(await ListHtml(area, $"{listScope}&Status=4&PaymentState=PartiallyPaid"));
        Check(completedIds.Contains(listCompleted) && !completedIds.Contains(staleId),
            $"{area} completed bookings can still be found by outstanding payment status");
        var legacyHtml = await ListHtml(area, $"{listScope}&Status=6");
        var paymentSelect = Regex.Match(legacyHtml, "<select[^>]*name=\"PaymentState\"[\\s\\S]*?</select>").Value;
        var bookingSelect = Regex.Match(legacyHtml, "<select[^>]*name=\"Status\"[\\s\\S]*?</select>").Value;
        Check(ListIds(legacyHtml).Contains(staleId) && !bookingSelect.Contains("value=\"6\"")
            && Regex.IsMatch(paymentSelect, "<option(?=[^>]*value=\"PartiallyPaid\")(?=[^>]*selected)[^>]*>"),
            $"{area} old partially-paid filter links redirect their selection to payment status");
        var limitHtml = await ListHtml(area, "Search=LIST-LIMIT&PaymentState=PartiallyPaid");
        Check(ListIds(limitHtml).Count == 1 && limitHtml.Contains("LIST-LIMIT-204"),
            $"{area} payment filter finds matching bookings beyond the first 200 candidates");
        Check(ListIds(await ListHtml(area, "Search=LIST-LIMIT")).Count == 200,
            $"{area} booking list retains its 200-result limit");
    }
    var transitionRef = (await Load(listTransition)).BookingReferenceNo;
    await Post(await Form(listTransition, "1400.00"));
    foreach (var area in new[] { "Admin", "Staff" })
    {
        Check(ListIds(await ListHtml(area, $"Search={transitionRef}&PaymentState=PartiallyPaid")).Count == 0
            && ListIds(await ListHtml(area, $"Search={transitionRef}&PaymentState=FullyPaid")).Contains(listTransition),
            $"{area} cash settlement moves booking from partially paid to fully paid list results");
    }

    var legacy = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.PartiallyPaid);
    await Post(await Form(legacy, "1400.00"));
    Check((await Load(legacy)).BookingStatus == BookingStatus.Confirmed && PaymentSummaryHelper.Calculate(await Load(legacy)).Status == "FullyPaid", "Legacy PartiallyPaid bookings can be settled without deleting data");
    var completed = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.Completed);
    await Post(await Form(completed, "1400.00"));
    Check((await Load(completed)).BookingStatus == BookingStatus.Completed, "Collecting a completed booking's balance preserves completed status");

    var cancelled = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.Cancelled);
    var blocked = new Dictionary<string, string>(form) { ["Id"] = cancelled.ToString(), ["SubmissionId"] = Guid.NewGuid().ToString() };
    await Post(blocked);
    Check((await Load(cancelled)).Payments.Count == 1, "Cancelled bookings reject collection");

    var raceId = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.Confirmed);
    var raceForm = await Form(raceId, "1000.00");
    var competing = new Dictionary<string, string>(raceForm) { ["SubmissionId"] = Guid.NewGuid().ToString() };
    var races = await Task.WhenAll(Post(raceForm), Post(competing));
    foreach (var response in races) response.Dispose();
    Check(PaymentSummaryHelper.Calculate(await Load(raceId)) is { AmountPaid: 2400m, RemainingBalance: 400m }
        && (await Load(raceId)).Payments.Count == 2, "Concurrent collections from separate forms cannot overpay");
    var duplicateId = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.Confirmed);
    var duplicateForm = await Form(duplicateId, "300.00");
    var duplicates = await Task.WhenAll(Post(duplicateForm), Post(duplicateForm));
    foreach (var response in duplicates) response.Dispose();
    Check((await Load(duplicateId)).Payments.Count == 2, "Concurrent identical submissions create one cash transaction");

    var rollbackId = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.Confirmed);
    var rollback = await Form(rollbackId, "1400.00");
    await Post(rollback, failCommit: true);
    Check((await Load(rollbackId)).Payments.Count == 1 && PaymentSummaryHelper.Calculate(await Load(rollbackId)).RemainingBalance == 1400m, "Commit failure rolls back cash payment and balance changes");
    await Post(rollback);
    Check(PaymentSummaryHelper.Calculate(await Load(rollbackId)).Status == "FullyPaid", "Failed transaction can be safely retried with the original key");

    var onlineId = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.Confirmed);
    var online = await Load(onlineId);
    var onlineForm = await Form(onlineId, "1400.00");
    using var start = await client.PostAsync("/Bookings/StartBalancePayment", new FormUrlEncodedContent(new Dictionary<string, string> {
        ["reference"] = online.BookingReferenceNo, ["__RequestVerificationToken"] = onlineForm["__RequestVerificationToken"] }));
    Check(start.StatusCode == HttpStatusCode.Redirect && start.Headers.Location!.ToString().Contains("PayMongoStart"), "Confirmed reservation retains existing online balance payment flow");
    using var checkout = await client.GetAsync(start.Headers.Location);
    Check(checkout.StatusCode == HttpStatusCode.OK && (await checkout.Content.ReadAsStringAsync()).Contains("paymongo"), "Existing balance checkout still renders");
    await Post(onlineForm);
    Check(!(await Load(onlineId)).Payments.Any(p => p.PaymentMethod == PaymentMethod.Cash), "Cash collection is blocked while online balance payment is pending");
    using var pending = await client.GetAsync($"/Bookings/PaymentPending?reference={online.BookingReferenceNo}");
    Check(pending.StatusCode == HttpStatusCode.OK, "Balance checkout does not appear paid just because booking is Confirmed");
    await WithDb(async db => {
        var payment = await db.Payments.SingleAsync(p => p.BookingId == onlineId && p.PaymentPurpose == PaymentPurpose.Balance);
        payment.CheckoutExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    });
    await using (var scope = app.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<PayMongoQrPhExpiryService>().CancelExpiredPaymentsAsync();
    Check((await Load(onlineId)).Payments.Single(p => p.PaymentPurpose == PaymentPurpose.Balance).PaymentStatus == PaymentStatus.Rejected
        && (await Load(onlineId)).BookingStatus == BookingStatus.Confirmed, "Online balance expiry keeps confirmed reservation active");
    await Post(onlineForm);
    Check(PaymentSummaryHelper.Calculate(await Load(onlineId)).Status == "FullyPaid", "Cash can settle balance after online checkout expires");
    await Webhook(onlineId, balance: true, paidAt: DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds());
    var delayed = (await Load(onlineId)).Payments.Single(p => p.PaymentPurpose == PaymentPurpose.Balance && p.PaymentMethod == PaymentMethod.PayMongoQrPh);
    Check(delayed.PaymentStatus == PaymentStatus.Rejected && delayed.GatewayTransactionId != null && delayed.RawWebhookPayload != null
        && PaymentSummaryHelper.Calculate(await Load(onlineId)).AmountPaid == 2800m,
        "Delayed online payment after cash settlement retains gateway audit without approving an excess balance");

    var mixedId = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.Confirmed);
    var mixedForm = await Form(mixedId, "400.00");
    await Post(mixedForm);
    var mixed = await Load(mixedId);
    using var mixedStart = await client.PostAsync("/Bookings/StartBalancePayment", new FormUrlEncodedContent(new Dictionary<string, string> {
        ["reference"] = mixed.BookingReferenceNo, ["__RequestVerificationToken"] = mixedForm["__RequestVerificationToken"] }));
    await client.GetAsync(mixedStart.Headers.Location);
    Check((await Load(mixedId)).Payments.Single(p => p.PaymentPurpose == PaymentPurpose.Balance && p.PaymentMethod == PaymentMethod.PayMongoQrPh).Amount == 1000m,
        "Online checkout after partial cash collection uses the recalculated balance");
    await Webhook(mixedId, balance: true);
    Check(PaymentSummaryHelper.Calculate(await Load(mixedId)).Status == "FullyPaid" && (await Load(mixedId)).BookingStatus == BookingStatus.Confirmed,
        "Existing online balance webhook settles a reservation plus partial cash payment");

    var checkoutRaceId = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.Confirmed);
    var checkoutRaceForm = await Form(checkoutRaceId, "1400.00");
    var checkoutRaceBooking = await Load(checkoutRaceId);
    var checkoutRaces = await Task.WhenAll(Post(checkoutRaceForm), client.PostAsync("/Bookings/StartBalancePayment", new FormUrlEncodedContent(new Dictionary<string, string> {
        ["reference"] = checkoutRaceBooking.BookingReferenceNo, ["__RequestVerificationToken"] = checkoutRaceForm["__RequestVerificationToken"] })));
    foreach (var response in checkoutRaces) response.Dispose();
    var racedBooking = await Load(checkoutRaceId);
    Check(racedBooking.Payments.Count == 2 &&
        (PaymentSummaryHelper.Calculate(racedBooking).RemainingBalance == 0m || !racedBooking.Payments.Any(p => p.PaymentMethod == PaymentMethod.Cash)),
        "Concurrent cash collection and online checkout creation cannot both claim the same balance");

    // Apply just the new migration to an existing schema, preserving records.
    await WithDb(async db =>
    {
        var legacyId = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.PartiallyPaid);
        var migrationIds = db.Database.GetMigrations().ToArray();
        if (sqlServer) await db.Database.ExecuteSqlRawAsync("CREATE TABLE [__EFMigrationsHistory] ([MigrationId] nvarchar(150) NOT NULL PRIMARY KEY, [ProductVersion] nvarchar(32) NOT NULL);");
        else await db.Database.ExecuteSqlRawAsync("CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL);");
        foreach (var migration in migrationIds[..^1])
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({migration}, {"8.0.0"});");
        await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Payments_CollectionSubmissionId ON Payments;".Replace(" ON Payments", sqlServer ? " ON Payments" : ""));
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Payments DROP COLUMN CollectionSubmissionId;");
        await db.GetService<IMigrator>().MigrateAsync();
        Check((await db.Bookings.SingleAsync(b => b.Id == legacyId)).BookingStatus == BookingStatus.Confirmed
            && await db.Payments.AnyAsync(p => p.BookingId == legacyId), "Additive EF migration preserves payments and confirms legacy reservations");
    });
    if (!sqlServer)
    {
        var upgradeId = await Seed(paymentStatus: PaymentStatus.Approved, bookingStatus: BookingStatus.PartiallyPaid);
        await WithDb(async db =>
        {
            var count = await db.Payments.CountAsync();
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Payments_CollectionSubmissionId;");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Payments DROP COLUMN CollectionSubmissionId;");
            async Task Invoke(string method) => await (Task)typeof(DbSeeder)
                .GetMethod(method, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new object[] { db })!;
            await Invoke("RemapLegacyStatusesIfNeededAsync");
            Check((await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == upgradeId)).BookingStatus == BookingStatus.PartiallyPaid
                && (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == cancelled)).BookingStatus == BookingStatus.Cancelled
                && (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == completed)).BookingStatus == BookingStatus.Completed,
                "SQLite restart skips legacy remap for current reservation, cancelled and completed statuses");
            await Invoke("EnsureSqliteCashCollectionSchemaAsync");
            await Invoke("EnsureSqliteCashCollectionSchemaAsync");
            Check(await db.Payments.CountAsync() == count &&
                (await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == upgradeId)).BookingStatus == BookingStatus.Confirmed,
                "SQLite EnsureCreated upgrade is additive and repeatable without losing payments");
        });
    }
    Console.WriteLine($"All {checks} integration checks passed ({(sqlServer ? "SQL Server" : "SQLite")}).");
}
finally
{
    await app.StopAsync();
    if (sqlServer) await WithDb(db => db.Database.EnsureDeletedAsync());
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    foreach (var suffix in new[] { "", "-wal", "-shm" })
        if (File.Exists(database + suffix)) File.Delete(database + suffix);
}

sealed class TestPayMongoClient : IPayMongoClient
{
    public Task<CheckoutSessionResponse> CreateQrPhCheckoutSessionAsync(Booking booking, Payment payment, CancellationToken cancellationToken = default) =>
        Task.FromResult(new CheckoutSessionResponse { Data = new() { Id = $"cs_test_{payment.Id}",
            Attributes = new() { CheckoutUrl = "https://paymongo.test/checkout", ReferenceNumber = booking.BookingReferenceNo } } });
    public Task ExpireCheckoutSessionAsync(string checkoutSessionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
