using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

internal static class BookingNotesChecks
{
    public static async Task RunAsync(WebApplication app, HttpClient client, Action<bool, string> check)
    {
        await CheckSchemaUpgradeAsync(check);
        async Task WithDb(Func<ApplicationDbContext, Task> action)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        async Task<string> Snapshot(int id, bool includeNotes = false)
        {
            var result = "";
            await WithDb(async db =>
            {
                var booking = await db.Bookings.Include(b => b.Payments).SingleAsync(b => b.Id == id);
                object Values(object entity, bool omitNotes = false) => db.Entry(entity).CurrentValues.Properties
                    .Where(p => !omitNotes || p.Name != nameof(Booking.Notes))
                    .ToDictionary(p => p.Name, p => db.Entry(entity).Property(p.Name).CurrentValue);
                result = JsonSerializer.Serialize(new { booking = Values(booking, !includeNotes),
                    payments = booking.Payments.OrderBy(p => p.Id).Select(p => Values(p)).ToArray() });
            });
            return result;
        }

        async Task<(string Html, string Token)> Details(int id)
        {
            var html = await client.GetStringAsync($"/Staff/Bookings/Details/{id}");
            var token = WebUtility.HtmlDecode(Regex.Match(html,
                "<input[^>]*name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
            return (html, token);
        }

        async Task<HttpResponseMessage> Save(int id, string? notes, string token, string role = "Staff", bool tamper = false)
        {
            var fields = new Dictionary<string, string> { ["Id"] = id.ToString(),
                ["Notes"] = notes ?? "", ["__RequestVerificationToken"] = token };
            if (tamper)
            {
                fields["BookingStatus"] = "Cancelled";
                fields["TotalAmount"] = "1";
                fields["IsWalkIn"] = "false";
                fields["CustomerName"] = "Tampered";
                fields["Payments[0].PaymentStatus"] = "Rejected";
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, "/Staff/Bookings/UpdateNotes")
                { Content = new FormUrlEncodedContent(fields) };
            request.Headers.Add("X-Test-Role", role);
            return await client.SendAsync(request);
        }

        int walkIn = 0, online = 0;
        await WithDb(async db =>
        {
            walkIn = await db.Bookings.Where(b => b.IsWalkIn && b.BookingStatus == BookingStatus.Confirmed)
                .Select(b => b.Id).FirstAsync();
            var booking = new Booking { BookingReferenceNo = "NOTES-ONLINE", CourtId = 1,
                CustomerName = "Online Guest", CustomerEmail = "online@test.local", CustomerMobile = "09123456789",
                BookingDate = PhilippineTime.Today.AddDays(30), StartTime = new(8, 0, 0), EndTime = new(9, 0, 0),
                BookingStatus = BookingStatus.Confirmed, TotalAmount = 1000,
                // Online notes and email are customer-controlled; neither grants edit eligibility.
                Notes = "Walk-in (counter) — customer supplied this text" };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync();
            online = booking.Id;
        });

        foreach (var status in new[] { BookingStatus.Confirmed, BookingStatus.Completed })
        {
            await WithDb(async db =>
            {
                var booking = await db.Bookings.FindAsync(walkIn);
                booking!.BookingStatus = status;
                booking.CustomerEmail = "real-email@test.local";
                await db.SaveChangesAsync();
            });
            var (html, token) = await Details(walkIn);
            check(html.Contains("id=\"editBookingNotes\"") && html.Contains("id=\"bookingNotesForm\"")
                && Regex.IsMatch(html, "<form[^>]*id=\"bookingNotesForm\"[^>]*hidden")
                && Regex.IsMatch(html, "<textarea[^>]*readonly"), $"{status} walk-in renders read-only notes and editing controls");
            check(html.Contains("maxlength=\"5000\"") && html.Contains("Maximum 5,000 characters."),
                $"{status} notes editor advertises and allows 5,000 characters");
            var before = await Snapshot(walkIn);
            using var response = await Save(walkIn, "  Updated notes\n<script>alert('notes')</script>  ", token, tamper: true);
            check(response.StatusCode == HttpStatusCode.OK, $"{status} walk-in with a real email accepts notes updates");
            var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            check(data.GetProperty("notes").GetString() == "Updated notes\n<script>alert('notes')</script>"
                && data.GetProperty("message").GetString()!.Contains("successfully"), "AJAX response returns saved notes and a success message");
            check(await Snapshot(walkIn) == before, $"{status} update preserves every other booking and payment field despite extra submitted fields");
            var after = await Details(walkIn);
            check(after.Html.Contains("&lt;script&gt;") && !after.Html.Contains("<script>alert('notes')</script>"), "Saved notes remain HTML encoded in Razor");

            var longNotes = new string('ñ', 5000);
            using var limit = await Save(walkIn, longNotes, token);
            check(limit.StatusCode == HttpStatusCode.OK, $"{status} walk-in accepts exactly 5,000 note characters");
            await WithDb(async db => check(await db.Bookings.Where(b => b.Id == walkIn).Select(b => b.Notes).SingleAsync() == longNotes,
                $"{status} update persists all 5,000 Unicode note characters without truncation"));
            check(await Snapshot(walkIn) == before, $"{status} long notes update leaves all other booking and payment fields unchanged");
        }

        var (_, validToken) = await Details(walkIn);
        using (var clear = await Save(walkIn, "  ", validToken))
            check(clear.StatusCode == HttpStatusCode.OK, "Staff can clear notes on a completed walk-in");
        var emptyPage = await Details(walkIn);
        check(emptyPage.Html.Contains("No notes.") && emptyPage.Html.Contains("id=\"editBookingNotes\""), "Clearing notes preserves walk-in identity and editing access");
        var unchanged = await Snapshot(walkIn, true);
        using (var tooLong = await Save(walkIn, new string('x', 5001), validToken))
            check(tooLong.StatusCode == HttpStatusCode.BadRequest, "Server rejects notes longer than 5,000 characters");
        using (var noToken = await Save(walkIn, "Missing CSRF token", ""))
            check(noToken.StatusCode == HttpStatusCode.BadRequest, "Notes update requires an antiforgery token");
        foreach (var role in new[] { "Member", "Anonymous" })
        {
            using var denied = await Save(walkIn, "Unauthorized", validToken, role);
            check(denied.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
                $"{role} cannot update staff booking notes");
        }
        await WithDb(async db => { (await db.Users.FindAsync("test-staff"))!.IsActive = false; await db.SaveChangesAsync(); });
        using (var inactive = await Save(walkIn, "Inactive", validToken))
            check(inactive.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden, "Inactive staff cannot update notes");
        await WithDb(async db => { (await db.Users.FindAsync("test-staff"))!.IsActive = true; await db.SaveChangesAsync(); });
        check(await Snapshot(walkIn, true) == unchanged, "Invalid and unauthorized requests leave notes, booking, and payment data unchanged");

        var onlinePage = await Details(online);
        check(!onlinePage.Html.Contains("id=\"editBookingNotes\"") && !onlinePage.Html.Contains("staff-booking-notes.js"), "Online guest with a walk-in marker keeps existing read-only UI");
        var onlineBefore = await Snapshot(online, true);
        using (var denied = await Save(online, "Try online edit", onlinePage.Token))
            check(denied.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden, "Server rejects a direct notes update for a non-walk-in booking");
        check(await Snapshot(online, true) == onlineBefore, "Rejected online edit leaves all persisted fields unchanged");
        using (var missing = await Save(int.MaxValue, "Missing booking", validToken))
            check(missing.StatusCode == HttpStatusCode.NotFound, "Missing booking returns 404");
        using (var admin = await Save(walkIn, "Admin note", validToken, "Admin"))
            check(admin.StatusCode == HttpStatusCode.OK, "Admin retains access to Staff walk-in notes updates");
    }

    private static async Task CheckSchemaUpgradeAsync(Action<bool, string> check)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:").Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE Bookings (Id INTEGER PRIMARY KEY, UserId TEXT NULL, Notes TEXT NULL);
            INSERT INTO Bookings (Id, UserId, Notes) VALUES
                (1, NULL, 'Walk-in (counter)'),
                (2, NULL, 'Walk-in (counter) — Uses a real customer email'),
                (3, NULL, 'Ordinary online guest note'),
                (4, 'member', 'Walk-in (counter)'),
                (5, NULL, NULL),
                (6, NULL, 'Walk-in (counter)feit');");
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL);");
        foreach (var migration in db.Database.GetMigrations().Where(id => string.CompareOrdinal(id, "20261108000000_AddBookingIsWalkIn") < 0))
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({migration}, {"8.0.0"});");
        await db.GetService<IMigrator>().MigrateAsync("20261108000000_AddBookingIsWalkIn");
        using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT group_concat(Id, ',') FROM (SELECT Id FROM Bookings WHERE IsWalkIn = 1 ORDER BY Id)";
        check((string)(await command.ExecuteScalarAsync())! == "1,2", "EF migration adds the walk-in flag and backfills existing records on SQLite");

        await db.Database.ExecuteSqlRawAsync("ALTER TABLE Bookings DROP COLUMN IsWalkIn;");
        var upgrade = typeof(DbSeeder).GetMethod("EnsureSqliteWalkInSchemaAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        await (Task)upgrade.Invoke(null, new object[] { db })!;
        check((string)(await command.ExecuteScalarAsync())! == "1,2", "Existing SQLite walk-ins are backfilled using the exact legacy marker without depending on email");

        await db.Database.ExecuteSqlRawAsync("UPDATE Bookings SET Notes = NULL WHERE Id = 1; UPDATE Bookings SET Notes = 'Walk-in (counter)' WHERE Id = 3;");
        await (Task)upgrade.Invoke(null, new object[] { db })!;
        check((string)(await command.ExecuteScalarAsync())! == "1,2", "Restart preserves walk-in identity after notes edits and does not repeat the backfill");

        using var notesCommand = db.Database.GetDbConnection().CreateCommand();
        notesCommand.CommandText = "SELECT Notes FROM Bookings WHERE Id = 2";
        var legacyNotes = await notesCommand.ExecuteScalarAsync();
        await db.GetService<IMigrator>().MigrateAsync();
        check(Equals(await notesCommand.ExecuteScalarAsync(), legacyNotes) && (string)(await command.ExecuteScalarAsync())! == "1,2",
            "Notes length migration preserves existing SQLite notes and walk-in flags");

        await using var sqlDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=UnusedScriptOnly;Trusted_Connection=True;").Options);
        var script = sqlDb.GetService<IMigrator>().GenerateScript("20261029060347_AddCashBalanceCollection", "20261108000000_AddBookingIsWalkIn");
        check(script.Contains("ADD [IsWalkIn] bit NOT NULL DEFAULT CAST(0 AS bit)") && script.Contains("UPDATE Bookings SET IsWalkIn = 1"),
            "SQL Server migration generates the additive flag and legacy backfill");
        var notesScript = sqlDb.GetService<IMigrator>().GenerateScript("20261108000000_AddBookingIsWalkIn", "20261108000100_ExpandBookingNotes");
        check(notesScript.Contains("ALTER TABLE [Bookings] ALTER COLUMN [Notes] nvarchar(max) NULL")
            && !notesScript.Contains("UPDATE Bookings") && !notesScript.Contains("DROP TABLE"),
            "SQL Server migration widens Notes storage without rewriting legacy bookings");
    }
}
