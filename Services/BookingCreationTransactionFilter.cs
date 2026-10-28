using System.Data;
using System.Data.Common;
using CourtBookingSystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services;

// All booking-create entry points must protect their availability reads as well
// as their writes. SQL Server holds predicate/range locks until commit; SQLite
// acquires its write reservation before the reads (non-deferred transaction).
public sealed class BookingCreationTransactionFilter(
    ApplicationDbContext db, ILogger<BookingCreationTransactionFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ActionExecutedContext? executed = null;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            executed = await next();
            if (executed.Exception != null && !executed.ExceptionHandled)
            {
                if (!IsDatabaseFailure(executed.Exception)) return;
                executed.ExceptionHandled = true;
                executed.Result = Failure(context, executed.Exception);
                return;
            }

            if (context.ModelState.IsValid && executed.Result is RedirectToActionResult)
                await transaction.CommitAsync();
            // Validation failures and other results are rolled back on disposal.
        }
        catch (Exception ex) when (IsDatabaseFailure(ex))
        {
            var failure = Failure(context, ex);
            if (executed != null) executed.Result = failure;
            else context.Result = failure;
        }
    }

    private IActionResult Failure(ActionExecutingContext context, Exception exception)
    {
        logger.LogWarning(exception, "Booking transaction failed; no bookings were saved.");
        db.ChangeTracker.Clear();
        var controller = (Controller)context.Controller;
        controller.TempData.Remove("Success");
        controller.ModelState.AddModelError("", "The bookings could not be saved. Availability may have changed or the database is busy. Review your selected dates and times and try again. No bookings were created.");
        return controller.View("Create", context.ActionArguments["vm"]);
    }

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is DbException or DbUpdateException;
}
