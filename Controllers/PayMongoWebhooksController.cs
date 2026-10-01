using System.Text;
using System.Text.Json;
using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services.PayMongo;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Controllers
{
    /// <summary>
    /// Receives PayMongo webhook events, verifies the Paymongo-Signature header,
    /// and applies status changes to the matching Booking + Payment.
    /// </summary>
    [ApiController]
    [Route("api/payments/paymongo")]
    public class PayMongoWebhooksController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IPayMongoWebhookVerifier _verifier;
        private readonly ILogger<PayMongoWebhooksController> _logger;

        public PayMongoWebhooksController(
            ApplicationDbContext db,
            IPayMongoWebhookVerifier verifier,
            ILogger<PayMongoWebhooksController> logger)
        {
            _db = db;
            _verifier = verifier;
            _logger = logger;
        }

        // POST /api/payments/paymongo/webhook
        [HttpPost("webhook")]
        public async Task<IActionResult> Webhook(CancellationToken ct)
        {
            Request.EnableBuffering();
            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync(ct);
            }

            var sigHeader = Request.Headers["Paymongo-Signature"].ToString();
            if (!_verifier.VerifySignature(sigHeader, rawBody, out var reason))
            {
                _logger.LogWarning("PayMongo webhook rejected: {Reason}", reason);
                return Unauthorized(new { error = reason });
            }

            WebhookPaymentResult webhook;
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                webhook = ParseWebhook(doc.RootElement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayMongo webhook payload could not be parsed");
                return BadRequest(new { error = "invalid json" });
            }

            if (string.IsNullOrWhiteSpace(webhook.EventType))
            {
                return Ok();
            }

            var eventType = webhook.EventType.ToLowerInvariant();
            if (eventType is not (
                "payment.paid"
                or "payment.failed"
                or "checkout_session.payment.paid"
                or "checkout_session.payment.failed"
                or "qrph.expired"
                or "qr.expired"))
            {
                _logger.LogInformation("PayMongo webhook ignored event '{Type}'", eventType);
                return Ok();
            }

            var payment = await FindPaymentAsync(webhook, ct);
            if (payment == null || payment.Booking == null)
            {
                _logger.LogWarning(
                    "PayMongo webhook could not match payment. Event={EventType}, PaymentId={PaymentId}, SessionId={SessionId}, BookingId={BookingId}, Reference={Reference}",
                    eventType,
                    webhook.PaymentId,
                    webhook.CheckoutSessionId,
                    webhook.BookingId,
                    webhook.BookingReference);
                return Ok();
            }

            var isPaidEvent = eventType is "payment.paid" or "checkout_session.payment.paid";
            var isFailedEvent = eventType is "payment.failed" or "checkout_session.payment.failed";
            var isExpiredEvent = eventType is "qrph.expired" or "qr.expired";

            if (isPaidEvent && payment.PaymentStatus == PaymentStatus.Approved) return Ok();
            if (isFailedEvent && payment.PaymentStatus == PaymentStatus.Rejected) return Ok();
            if (isExpiredEvent && payment.PaymentStatus == PaymentStatus.Rejected) return Ok();
            if ((isFailedEvent || isExpiredEvent) && payment.PaymentStatus == PaymentStatus.Approved) return Ok();

            payment.GatewayTransactionId = webhook.PaymentId ?? payment.GatewayTransactionId;
            payment.QrReference ??= webhook.CheckoutSessionId;
            payment.ReferenceNo ??= webhook.BookingReference;
            payment.RawWebhookPayload = rawBody.Length > 8000 ? rawBody[..8000] : rawBody;

            string? notifyTitle = null;
            string? notifyMessage = null;

            if (isPaidEvent)
            {
                var paidDate = webhook.PaidAtUnix.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(webhook.PaidAtUnix.Value).UtcDateTime
                    : DateTime.UtcNow;

                payment.PaidDate = paidDate;

                var checkoutExpiresAtUtc = payment.CheckoutExpiresAtUtc
                    ?? (!string.IsNullOrWhiteSpace(payment.CheckoutUrl)
                        ? payment.Booking.CreatedDate.Add(CourtBookingSystem.Services.PayMongoQrPhExpiryService.CheckoutLifetime)
                        : null);

                if (checkoutExpiresAtUtc.HasValue && paidDate > checkoutExpiresAtUtc.Value)
                {
                    payment.CheckoutExpiresAtUtc ??= checkoutExpiresAtUtc;
                    payment.PaymentStatus = PaymentStatus.Rejected;
                    payment.CheckoutUrl = null;
                    if (ShouldCancelBookingOnRejectedPayment(payment))
                    {
                        payment.Booking.BookingStatus = BookingStatus.Cancelled;
                    }
                    notifyTitle = "Late PayMongo Payment Received";
                    var expiryMinutes = (int)CourtBookingSystem.Services.PayMongoQrPhExpiryService.CheckoutLifetime.TotalMinutes;
                    notifyMessage = ShouldCancelBookingOnRejectedPayment(payment)
                        ? $"Payment for booking {payment.Booking.BookingReferenceNo} arrived after the {expiryMinutes}-minute QR Ph expiry and the booking remains cancelled."
                        : $"Balance payment for booking {payment.Booking.BookingReferenceNo} arrived after the {expiryMinutes}-minute QR Ph expiry and was rejected; the partially paid booking remains active.";
                }
                else
                {
                    payment.PaymentStatus = PaymentStatus.Approved;
                    payment.ConfirmedDate = DateTime.UtcNow;
                    var summary = Services.PaymentSummaryHelper.Calculate(payment.Booking);
                    payment.Booking.BookingStatus = summary.RemainingBalance <= 0m
                        ? BookingStatus.Confirmed
                        : payment.PaymentPurpose == PaymentPurpose.Reservation
                            ? BookingStatus.PartiallyPaid
                            : payment.Booking.BookingStatus;
                    notifyTitle = "PayMongo Payment Received";
                    notifyMessage = payment.Booking.BookingStatus == BookingStatus.PartiallyPaid
                        ? $"Booking {payment.Booking.BookingReferenceNo} reservation payment received via PayMongo QR Ph."
                        : $"Booking {payment.Booking.BookingReferenceNo} auto-confirmed via PayMongo QR Ph.";
                }
            }
            else if (isFailedEvent)
            {
                payment.PaymentStatus = PaymentStatus.Rejected;
                payment.CheckoutUrl = null;
                if (ShouldCancelBookingOnRejectedPayment(payment))
                {
                    payment.Booking.BookingStatus = BookingStatus.Cancelled;
                }
                notifyTitle = "PayMongo Payment Failed";
                notifyMessage = $"Payment for booking {payment.Booking.BookingReferenceNo} failed via PayMongo.";
            }
            else if (isExpiredEvent)
            {
                payment.PaymentStatus = PaymentStatus.Rejected;
                payment.CheckoutUrl = null;
                if (ShouldCancelBookingOnRejectedPayment(payment))
                {
                    payment.Booking.BookingStatus = BookingStatus.Cancelled;
                }
                notifyTitle = "PayMongo QR Expired";
                notifyMessage = $"QR payment for booking {payment.Booking.BookingReferenceNo} expired before payment.";
            }

            await _db.SaveChangesAsync(ct);

            if (notifyTitle != null)
            {
                await Services.NotificationService.NotifyAsync(
                    _db,
                    title: notifyTitle,
                    message: notifyMessage!,
                    link: $"/Admin/Bookings/Details/{payment.Booking.Id}",
                    relatedBookingId: payment.Booking.Id);
            }

            return Ok(new { ok = true });
        }

        private async Task<Payment?> FindPaymentAsync(WebhookPaymentResult webhook, CancellationToken ct)
        {
            if (webhook.LocalPaymentId > 0)
            {
                var byLocalPaymentId = await _db.Payments
                    .Include(p => p.Booking)
                        .ThenInclude(b => b!.Payments)
                    .FirstOrDefaultAsync(p => p.Id == webhook.LocalPaymentId
                        && p.PaymentMethod == PaymentMethod.PayMongoQrPh, ct);
                if (byLocalPaymentId != null) return byLocalPaymentId;
            }

            if (!string.IsNullOrWhiteSpace(webhook.PaymentId))
            {
                var byGatewayId = await _db.Payments
                    .Include(p => p.Booking)
                        .ThenInclude(b => b!.Payments)
                    .FirstOrDefaultAsync(p => p.GatewayTransactionId == webhook.PaymentId, ct);
                if (byGatewayId != null) return byGatewayId;
            }

            if (!string.IsNullOrWhiteSpace(webhook.CheckoutSessionId))
            {
                var bySession = await _db.Payments
                    .Include(p => p.Booking)
                        .ThenInclude(b => b!.Payments)
                    .Where(p => p.QrReference == webhook.CheckoutSessionId
                        && p.PaymentMethod == PaymentMethod.PayMongoQrPh)
                    .FirstOrDefaultAsync(ct);
                if (bySession != null) return bySession;
            }

            if (webhook.BookingId > 0)
            {
                var byBookingIdQuery = _db.Payments
                    .Include(p => p.Booking)
                        .ThenInclude(b => b!.Payments)
                    .Where(p => p.BookingId == webhook.BookingId
                        && p.PaymentMethod == PaymentMethod.PayMongoQrPh);

                if (TryParsePaymentPurpose(webhook.PaymentPurpose, out var purpose))
                {
                    byBookingIdQuery = byBookingIdQuery.Where(p => p.PaymentPurpose == purpose);
                }

                var byBookingId = await byBookingIdQuery
                    .OrderByDescending(p => p.PaymentPurpose == PaymentPurpose.Reservation)
                    .ThenByDescending(p => p.Id)
                    .FirstOrDefaultAsync(ct);
                if (byBookingId != null) return byBookingId;
            }

            if (!string.IsNullOrWhiteSpace(webhook.BookingReference))
            {
                return await _db.Payments
                    .Include(p => p.Booking)
                        .ThenInclude(b => b!.Payments)
                    .Where(p => p.PaymentMethod == PaymentMethod.PayMongoQrPh
                        && p.PaymentPurpose == PaymentPurpose.FullPayment
                        && (p.Booking!.BookingReferenceNo == webhook.BookingReference
                            || p.ReferenceNo == webhook.BookingReference))
                    .FirstOrDefaultAsync(ct);
            }

            return null;
        }

        private static bool ShouldCancelBookingOnRejectedPayment(Payment payment)
        {
            return payment.PaymentPurpose != PaymentPurpose.Balance;
        }

        private static bool TryParsePaymentPurpose(string? value, out PaymentPurpose purpose)
        {
            return Enum.TryParse(value, ignoreCase: true, out purpose)
                && Enum.IsDefined(typeof(PaymentPurpose), purpose);
        }

        private static WebhookPaymentResult ParseWebhook(JsonElement root)
        {
            var data = GetObject(root, "data");
            var eventId = GetString(data, "id");
            var eventType = GetString(data, "type");
            var resource = GetObject(data, "data");

            if (string.IsNullOrWhiteSpace(eventType)
                || string.Equals(eventType, "event", StringComparison.OrdinalIgnoreCase))
            {
                var attributes = GetObject(data, "attributes");
                eventType = GetString(attributes, "type") ?? eventType;
                resource = GetObject(attributes, "data");
            }

            var resourceType = GetString(resource, "type");
            var resourceId = GetString(resource, "id");
            var resourceAttributes = GetObject(resource, "attributes");

            if (string.Equals(resourceType, "checkout_session", StringComparison.OrdinalIgnoreCase))
            {
                return ParseCheckoutSessionWebhook(eventId, eventType, resourceId, resourceAttributes);
            }

            if (string.Equals(resourceType, "qrph", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, "qr", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, "qr_code", StringComparison.OrdinalIgnoreCase))
            {
                return ParseQrWebhook(eventId, eventType, resourceId, resourceAttributes);
            }

            return ParsePaymentWebhook(eventId, eventType, resourceId, resourceAttributes);
        }

        private static WebhookPaymentResult ParseCheckoutSessionWebhook(
            string? eventId,
            string? eventType,
            string? checkoutSessionId,
            JsonElement attributes)
        {
            var metadata = GetObject(attributes, "metadata");
            var bookingId = GetInt(metadata, "bookingId");
            var localPaymentId = GetInt(metadata, "paymentId");
            var paymentPurpose = GetString(metadata, "paymentPurpose");
            var bookingReference = GetString(metadata, "bookingReference")
                ?? GetString(attributes, "reference_number");
            var paymentIntentId = GetString(GetObject(attributes, "payment_intent"), "id");

            string? paymentId = null;
            long? paidAt = null;

            if (TryGetPropertyIgnoreCase(attributes, "payments", out var payments)
                && payments.ValueKind == JsonValueKind.Array)
            {
                JsonElement? selectedPayment = null;
                foreach (var item in payments.EnumerateArray())
                {
                    selectedPayment = item;
                    var paymentStatus = GetString(GetObject(item, "attributes"), "status");
                    if (string.Equals(paymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                if (selectedPayment.HasValue)
                {
                    var payment = selectedPayment.Value;
                    var paymentAttributes = GetObject(payment, "attributes");
                    paymentId = GetString(payment, "id");
                    paidAt = GetLong(paymentAttributes, "paid_at")
                        ?? GetLong(paymentAttributes, "updated_at")
                        ?? GetLong(paymentAttributes, "created_at");
                    bookingReference ??= GetString(paymentAttributes, "external_reference_number");
                    paymentIntentId ??= GetString(paymentAttributes, "payment_intent_id");
                    var paymentMetadata = GetObject(paymentAttributes, "metadata");
                    localPaymentId = localPaymentId > 0 ? localPaymentId : GetInt(paymentMetadata, "paymentId");
                    paymentPurpose ??= GetString(paymentMetadata, "paymentPurpose");
                }
            }

            return new WebhookPaymentResult(
                eventId,
                eventType,
                paymentId ?? paymentIntentId,
                checkoutSessionId,
                localPaymentId,
                bookingId,
                bookingReference,
                paymentPurpose,
                paidAt);
        }

        private static WebhookPaymentResult ParsePaymentWebhook(
            string? eventId,
            string? eventType,
            string? paymentId,
            JsonElement attributes)
        {
            var metadata = GetObject(attributes, "metadata");
            var bookingId = GetInt(metadata, "bookingId");
            var localPaymentId = GetInt(metadata, "paymentId");
            var paymentPurpose = GetString(metadata, "paymentPurpose");
            var bookingReference = GetString(metadata, "bookingReference")
                ?? GetString(attributes, "external_reference_number");
            var checkoutSessionId = GetString(GetObject(attributes, "source"), "id");
            var paidAt = GetLong(attributes, "paid_at")
                ?? GetLong(attributes, "updated_at")
                ?? GetLong(attributes, "created_at");

            return new WebhookPaymentResult(
                eventId,
                eventType,
                paymentId,
                checkoutSessionId,
                localPaymentId,
                bookingId,
                bookingReference,
                paymentPurpose,
                paidAt);
        }

        private static WebhookPaymentResult ParseQrWebhook(
            string? eventId,
            string? eventType,
            string? qrId,
            JsonElement attributes)
        {
            var metadata = GetObject(attributes, "metadata");
            var bookingId = GetInt(metadata, "bookingId");
            var localPaymentId = GetInt(metadata, "paymentId");
            var paymentPurpose = GetString(metadata, "paymentPurpose");
            var bookingReference = GetString(metadata, "bookingReference")
                ?? GetString(attributes, "reference_number")
                ?? GetString(attributes, "external_reference_number");
            var paymentIntentId = GetString(attributes, "payment_intent_id")
                ?? GetString(GetObject(attributes, "payment_intent"), "id");
            var sourceId = GetString(GetObject(attributes, "source"), "id");

            return new WebhookPaymentResult(
                eventId,
                eventType,
                paymentIntentId ?? qrId,
                sourceId ?? qrId,
                localPaymentId,
                bookingId,
                bookingReference,
                paymentPurpose,
                null);
        }

        private static JsonElement GetObject(JsonElement parent, string propertyName)
        {
            return TryGetPropertyIgnoreCase(parent, propertyName, out var value)
                && value.ValueKind == JsonValueKind.Object
                    ? value
                    : default;
        }

        private static string? GetString(JsonElement parent, string propertyName)
        {
            return TryGetPropertyIgnoreCase(parent, propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }

        private static int GetInt(JsonElement parent, string propertyName)
        {
            if (!TryGetPropertyIgnoreCase(parent, propertyName, out var value))
            {
                return 0;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
                ? number
                : 0;
        }

        private static long? GetLong(JsonElement parent, string propertyName)
        {
            if (!TryGetPropertyIgnoreCase(parent, propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            {
                return number;
            }

            return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number)
                ? number
                : null;
        }

        private static bool TryGetPropertyIgnoreCase(JsonElement parent, string propertyName, out JsonElement value)
        {
            if (parent.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in parent.EnumerateObject())
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private sealed record WebhookPaymentResult(
            string? EventId,
            string? EventType,
            string? PaymentId,
            string? CheckoutSessionId,
            int LocalPaymentId,
            int BookingId,
            string? BookingReference,
            string? PaymentPurpose,
            long? PaidAtUnix);
    }
}
