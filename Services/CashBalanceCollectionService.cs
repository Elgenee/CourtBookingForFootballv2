using System.Data;
using System.Data.Common;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services;

public sealed class CashBalanceCollectionService(ApplicationDbContext db, ILogger<CashBalanceCollectionService> logger)
{
    public async Task<CashCollectionResult> CollectAsync(int bookingId, decimal amount, Guid submissionId,
        string staffId, CancellationToken ct = default)
    {
        if (submissionId == Guid.Empty || string.IsNullOrWhiteSpace(staffId))
            return new(false, "The payment form is invalid. Reload the booking and try again.");
        if (amount <= 0m || amount != decimal.Round(amount, 2) || amount > 9999999999999999.99m)
            return new(false, "Enter a positive cash amount with at most two decimal places.");

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var booking = await BookingPaymentLock.Query(db).SingleOrDefaultAsync(b => b.Id == bookingId, ct);
            if (booking == null) return new(false, "Booking not found.");
            await db.Entry(booking).Collection(b => b.Payments).LoadAsync(ct);

            var existing = await db.Payments.SingleOrDefaultAsync(p => p.CollectionSubmissionId == submissionId, ct);
            if (existing != null)
                return existing.BookingId == bookingId && existing.Amount == amount
                    ? new(true, "This cash payment has already been recorded.")
                    : new(false, "This payment form was already used. Reload the booking before recording another payment.");

            if (!PaymentSummaryHelper.CanCollectBalance(booking))
                return new(false, "Cash balance collection requires a paid reservation and an outstanding balance on a confirmed or completed booking.");

            // A checkout may still receive a verified webhook even after the
            // customer leaves its page. Wait for the existing expiry workflow.
            if (booking.Payments.Any(p => p.PaymentMethod == PaymentMethod.PayMongoQrPh
                && p.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Submitted))
                return new(false, "An online payment is still pending. Wait for confirmation or checkout expiry before collecting cash.");

            var summary = PaymentSummaryHelper.Calculate(booking);
            if (amount > summary.RemainingBalance)
                return new(false, $"The amount exceeds the outstanding balance of PHP {summary.RemainingBalance:0.00}. Reload the booking and review the balance.");

            var now = DateTime.UtcNow;
            booking.Payments.Add(new Payment
            {
                Amount = amount,
                PaymentMethod = PaymentMethod.Cash,
                PaymentPurpose = PaymentPurpose.Balance,
                PaymentStatus = PaymentStatus.Approved,
                PaidDate = now,
                ConfirmedDate = now,
                ConfirmedByUserId = staffId,
                CollectionSubmissionId = submissionId,
                ReferenceNo = $"CASH-{submissionId:N}"
            });
            if (booking.BookingStatus == BookingStatus.PartiallyPaid)
                booking.BookingStatus = BookingStatus.Confirmed;

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(true, $"Cash payment of PHP {amount:0.00} recorded. {PaymentSummaryHelper.Calculate(booking).StatusLabel}.");
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException)
        {
            logger.LogWarning(ex, "Cash collection transaction failed for booking {BookingId}", bookingId);
            db.ChangeTracker.Clear();
            return new(false, "The payment could not be confirmed. Reload the booking and check its payment history before trying again.");
        }
    }
}

public sealed record CashCollectionResult(bool Success, string Message);
