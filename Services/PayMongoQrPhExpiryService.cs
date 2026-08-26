using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services.PayMongo;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services
{
    public class PayMongoQrPhExpiryService
    {
        public static readonly TimeSpan CheckoutLifetime = TimeSpan.FromMinutes(3);

        private readonly ApplicationDbContext _db;
        private readonly IPayMongoClient _payMongo;
        private readonly ILogger<PayMongoQrPhExpiryService> _logger;

        public PayMongoQrPhExpiryService(
            ApplicationDbContext db,
            IPayMongoClient payMongo,
            ILogger<PayMongoQrPhExpiryService> logger)
        {
            _db = db;
            _payMongo = payMongo;
            _logger = logger;
        }

        public DateTime CreateExpiryUtc(DateTime utcNow) => utcNow.Add(CheckoutLifetime);

        public DateTime? GetEffectiveExpiryUtc(Booking booking, Payment payment)
        {
            if (payment.CheckoutExpiresAtUtc.HasValue)
            {
                return payment.CheckoutExpiresAtUtc.Value;
            }

            return !string.IsNullOrWhiteSpace(payment.CheckoutUrl)
                ? booking.CreatedDate.Add(CheckoutLifetime)
                : null;
        }

        public bool IsExpired(Booking booking, Payment payment, DateTime utcNow)
        {
            var expiresAtUtc = GetEffectiveExpiryUtc(booking, payment);
            return expiresAtUtc.HasValue && expiresAtUtc.Value <= utcNow;
        }

        public async Task<bool> CancelIfExpiredAsync(
            Booking booking,
            Payment payment,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            if (!ShouldCancel(payment, booking, now))
            {
                return false;
            }

            await ExpirePayMongoSessionAsync(payment, cancellationToken);
            CancelLocally(booking, payment, now);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Cancelled expired PayMongo QR Ph booking {Ref}", booking.BookingReferenceNo);
            return true;
        }

        public async Task<int> CancelExpiredPaymentsAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var expiredPayments = await _db.Payments
                .Include(p => p.Booking)
                .Where(p => p.PaymentMethod == PaymentMethod.PayMongoQrPh
                    && ((p.CheckoutExpiresAtUtc.HasValue && p.CheckoutExpiresAtUtc.Value <= now)
                        || (p.CheckoutExpiresAtUtc == null
                            && p.CheckoutUrl != null
                            && p.Booking != null
                            && p.Booking.CreatedDate <= now.Subtract(CheckoutLifetime)))
                    && (p.PaymentStatus == PaymentStatus.Unpaid || p.PaymentStatus == PaymentStatus.Submitted)
                    && p.Booking != null
                    && p.Booking.BookingStatus != BookingStatus.Cancelled
                    && p.Booking.BookingStatus != BookingStatus.Confirmed)
                .ToListAsync(cancellationToken);

            foreach (var payment in expiredPayments)
            {
                if (payment.Booking == null || !ShouldCancel(payment, payment.Booking, now))
                {
                    continue;
                }

                await ExpirePayMongoSessionAsync(payment, cancellationToken);
                CancelLocally(payment.Booking, payment, now);
            }

            if (expiredPayments.Count > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            return expiredPayments.Count;
        }

        private bool ShouldCancel(Payment payment, Booking booking, DateTime utcNow)
        {
            return payment.PaymentMethod == PaymentMethod.PayMongoQrPh
                && payment.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Submitted
                && booking.BookingStatus is not BookingStatus.Cancelled and not BookingStatus.Confirmed
                && IsExpired(booking, payment, utcNow);
        }

        private void CancelLocally(Booking booking, Payment payment, DateTime utcNow)
        {
            payment.CheckoutExpiresAtUtc ??= GetEffectiveExpiryUtc(booking, payment) ?? utcNow;
            payment.PaymentStatus = PaymentStatus.Rejected;
            payment.CheckoutUrl = null;
            payment.ConfirmedDate ??= utcNow;
            booking.BookingStatus = BookingStatus.Cancelled;
        }

        private async Task ExpirePayMongoSessionAsync(Payment payment, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(payment.QrReference))
            {
                return;
            }

            try
            {
                await _payMongo.ExpireCheckoutSessionAsync(payment.QrReference, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not expire PayMongo Checkout Session {SessionId}; cancelling booking locally.",
                    payment.QrReference);
            }
        }
    }
}
