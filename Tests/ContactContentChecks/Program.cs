using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CourtBookingSystem.Areas.SuperAdmin.Controllers;
using CourtBookingSystem.Areas.SuperAdmin.ViewModels;
using CourtBookingSystem.Data;
using CourtBookingSystem.Migrations;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Cms;
using CourtBookingSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

// Exercise schema upgrades and CMS persistence using a disposable database.
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
    Console.WriteLine($"PASS {message}");
}
async Task UpgradeSchema(ApplicationDbContext db) => await (Task)typeof(DbSeeder)
    .GetMethod("EnsureSqliteCmsSchemaAsync", BindingFlags.NonPublic | BindingFlags.Static)!
    .Invoke(null, new object[] { db })!;

await using (var db = new ApplicationDbContext(options))
{
    await db.Database.EnsureCreatedAsync();
    db.AboutContents.Add(new()
    {
        Title = "About CAMPO TURF", Description = "Our turf", ContactEmail = "existing@example.com",
        ContactPhone = CampoContactDetails.SchedulingPhone, Location = CampoContactDetails.Address,
        OpeningHoursDays = "Mon – Sun", OpeningHoursTimes = "6 AM – 4 AM"
    });
    await db.SaveChangesAsync();
    foreach (var command in new[]
    {
        "ALTER TABLE AboutContents DROP COLUMN SchedulingContactName;",
        "ALTER TABLE AboutContents DROP COLUMN EventsPhone;",
        "ALTER TABLE AboutContents DROP COLUMN EventsContactName;",
        "ALTER TABLE AboutContents DROP COLUMN FacebookName;",
        "ALTER TABLE AboutContents DROP COLUMN FacebookUrl;",
        "ALTER TABLE AboutContents DROP COLUMN InstagramHandle;",
        "ALTER TABLE AboutContents DROP COLUMN InstagramUrl;"
    })
        await db.Database.ExecuteSqlRawAsync(command);
    await UpgradeSchema(db);
}

await using (var db = new ApplicationDbContext(options))
{
    var about = await new SiteContentService(db).GetAboutAsync();
    Check(about.SchedulingContactName == "Jenny" && about.EventsContactName == "Catrina", "Existing databases receive both contact names");
    Check(about.EventsPhone == "0915 619 0673", "Events number is persisted by the upgrade");
    Check(about.FacebookUrl == CampoContactDetails.FacebookUrl && about.FacebookName == "Campo Cebu", "Facebook name and URL are persisted");
    Check(about.InstagramUrl == CampoContactDetails.InstagramUrl && about.InstagramHandle == "@campocebu", "Instagram handle and URL are persisted");
    Check(about.ContactEmail == "existing@example.com" && about.OpeningHoursTimes == "6 AM – 4 AM", "Schema upgrade retains existing contact and hours settings");

    var http = new DefaultHttpContext();
    var controller = new AboutController(db)
    {
        ControllerContext = new ControllerContext { HttpContext = http },
        TempData = new TempDataDictionary(http, new MemoryTempDataProvider())
    };
    var form = (AboutContentViewModel)((ViewResult)await controller.Index()).Model!;
    Check(form.EventsPhone == about.EventsPhone && form.InstagramUrl == about.InstagramUrl, "Admin editor loads saved contact values");
    form.SchedulingContactName = " New scheduler ";
    form.ContactPhone = " 0999 111 2222 ";
    form.EventsContactName = " New events contact ";
    form.EventsPhone = " 0999 333 4444 ";
    form.FacebookName = " Updated page ";
    form.FacebookUrl = " https://www.facebook.com/updated-page/ ";
    form.InstagramHandle = " @updatedhandle ";
    form.InstagramUrl = " https://www.instagram.com/updatedhandle/ ";
    form.Location = " Updated address ";
    Check(await controller.Index(form) is RedirectToActionResult, "Admin save succeeds");
}

await using (var db = new ApplicationDbContext(options))
{
    await UpgradeSchema(db);
    var about = await new SiteContentService(db).GetAboutAsync();
    Check(about.SchedulingContactName == "New scheduler" && about.ContactPhone == "0999 111 2222", "Scheduling edits survive a new request and repeated schema upgrade");
    Check(about.EventsContactName == "New events contact" && about.EventsPhone == "0999 333 4444", "Events edits are saved and trimmed");
    Check(about.FacebookName == "Updated page" && about.FacebookUrl == "https://www.facebook.com/updated-page/", "Facebook edits are saved and trimmed");
    Check(about.InstagramHandle == "@updatedhandle" && about.InstagramUrl == "https://www.instagram.com/updatedhandle/", "Instagram edits are saved and trimmed");
    Check(about.Location == "Updated address", "Address remains database controlled");
    var tracked = await db.AboutContents.SingleAsync();
    tracked.InstagramUrl = null;
    await db.SaveChangesAsync();
    await UpgradeSchema(db);
}
await using (var db = new ApplicationDbContext(options))
    Check((await new SiteContentService(db).GetAboutAsync()).InstagramUrl == null, "Clearing a social URL is retained on later startups");

var validation = new List<ValidationResult>();
var invalid = new AboutContentViewModel { Title = "About", Description = "Turf", FacebookUrl = "javascript:alert(1)" };
Check(!Validator.TryValidateObject(invalid, new ValidationContext(invalid), validation, true)
    && validation.Any(v => v.MemberNames.Contains(nameof(invalid.FacebookUrl))), "Social URL validation rejects unsafe schemes");

await using (var sql = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=ContactMigrationCheck;Trusted_Connection=True").Options))
{
    var script = sql.GetService<IMigrator>().GenerateScript("20261015070000_ExtendOperatingHours", "20261020033437_AddContactChannels");
    Check(script.Contains("ADD [InstagramUrl]") && script.Contains("SchedulingContactName = 'Jenny'"), "SQL Server migration adds and populates the contact fields");
    Check(!script.Contains("CREATE TABLE [BookingPricingRules]") && !script.Contains("DROP TABLE"), "Contact migration leaves booking tables untouched");
}
Console.WriteLine($"{checks} contact content checks passed.");

sealed class MemoryTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
