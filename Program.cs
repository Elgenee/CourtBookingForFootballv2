using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

Environment.SetEnvironmentVariable("TZ", PhilippineTime.IanaTimeZoneId);
TimeZoneInfo.ClearCachedData();

var builder = WebApplication.CreateBuilder(args);

// ----- Database -----
// Default provider is SQL Server. For cross-platform local previews you can set
// DbProvider=Sqlite in environment / appsettings to use a local .db file instead.
var dbProvider = builder.Configuration["DbProvider"] ?? "SqlServer";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (string.Equals(dbProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        var sqliteConn = builder.Configuration.GetConnectionString("SqliteConnection")
            ?? "Data Source=CourtBooking.db";
        options.UseSqlite(sqliteConn);
    }
    else
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        options.UseSqlServer(connectionString);
    }
});

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ----- Identity -----
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        // Password policy
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 8;

        // User settings
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ----- Cookie / Authentication -----
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// ----- MVC -----
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// ----- App services -----
builder.Services.AddScoped<CourtBookingSystem.Services.BookingAvailabilityService>();
builder.Services.AddScoped<CourtBookingSystem.Services.CourtGroupAvailabilityService>();
builder.Services.AddScoped<CourtBookingSystem.Services.BookingPricingService>();
builder.Services.AddScoped<CourtBookingSystem.Services.SiteContentService>();
builder.Services.AddScoped<CourtBookingSystem.Services.PayMongoQrPhExpiryService>();
builder.Services.AddHostedService<CourtBookingSystem.Services.PayMongoQrPhExpiryBackgroundService>();
builder.Services.Configure<CourtBookingSystem.Services.FootballPaymentOptions>(
    builder.Configuration.GetSection("FootballPayments"));

// ----- PayMongo QR Ph integration (additive — does not affect existing manual GCash) -----
builder.Services.Configure<CourtBookingSystem.Services.PayMongo.PayMongoOptions>(
    builder.Configuration.GetSection("PayMongo"));
builder.Services.AddSingleton<CourtBookingSystem.Services.PayMongo.IPayMongoWebhookVerifier,
                              CourtBookingSystem.Services.PayMongo.PayMongoWebhookVerifier>();
builder.Services.AddHttpClient<CourtBookingSystem.Services.PayMongo.IPayMongoClient,
                               CourtBookingSystem.Services.PayMongo.PayMongoClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<
        CourtBookingSystem.Services.PayMongo.PayMongoOptions>>().Value;
    var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl) ? "https://api.paymongo.com" : opts.BaseUrl;
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    if (!string.IsNullOrWhiteSpace(opts.SecretKey))
    {
        client.DefaultRequestHeaders.Authorization =
            CourtBookingSystem.Services.PayMongo.PayMongoClient.BuildAuthHeader(opts.SecretKey);
    }
});
builder.Services.AddScoped<CourtBookingSystem.Services.ReceiptPdfService>();

// ----- QuestPDF license (Community = free) -----
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var app = builder.Build();

// ----- Pipeline -----
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

// ----- Seed database (roles, admin, business hours, courts) -----
using (var scope = app.Services.CreateScope())
{
    try
    {
        await DbSeeder.SeedAsync(app.Services, app.Configuration);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();
