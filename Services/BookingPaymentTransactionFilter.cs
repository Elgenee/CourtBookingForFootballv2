using System.Data;
using System.Data.Common;
using CourtBookingSystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services;

// Checkout creation and cash collection must read balances under the same
// database lock, including when their requests reach different app instances.
public sealed class BookingPaymentTransactionFilter(ApplicationDbContext db,
    ILogger<BookingPaymentTransactionFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ActionExecutedContext? executed = null;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            if (context.ActionArguments.TryGetValue("reference", out var reference) && reference is string value)
                await BookingPaymentLock.Query(db).SingleOrDefaultAsync(b => b.BookingReferenceNo == value);

            executed = await next();
            if (executed.Exception != null && !executed.ExceptionHandled)
            {
                if (executed.Exception is not (DbException or DbUpdateException)) return;
                executed.ExceptionHandled = true;
                executed.Result = Failure(executed.Exception);
                return;
            }
            await transaction.CommitAsync();
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            var result = Failure(ex);
            if (executed != null) executed.Result = result;
            else context.Result = result;
        }
    }

    private IActionResult Failure(Exception ex)
    {
        logger.LogWarning(ex, "Payment checkout transaction failed.");
        db.ChangeTracker.Clear();
        return new ConflictObjectResult("The payment changed or the database is busy. Reload your booking to review its payments before trying again.");
    }
}
