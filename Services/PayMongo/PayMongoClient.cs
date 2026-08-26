using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CourtBookingSystem.Models;
using Microsoft.Extensions.Options;

namespace CourtBookingSystem.Services.PayMongo
{
    /// <summary>
    /// Thin typed client around PayMongo's Checkout Sessions endpoint. Uses
    /// Basic Auth with the secret key (PayMongo's documented scheme). All
    /// PayMongo HTTP details live in this class so the rest of the app talks
    /// in domain terms only (Booking + Payment).
    /// </summary>
    public interface IPayMongoClient
    {
        Task<CheckoutSessionResponse> CreateQrPhCheckoutSessionAsync(
            Booking booking,
            decimal amount,
            CancellationToken cancellationToken = default);

        Task ExpireCheckoutSessionAsync(
            string checkoutSessionId,
            CancellationToken cancellationToken = default);
    }

    public class PayMongoClient : IPayMongoClient
    {
        private readonly HttpClient _http;
        private readonly PayMongoOptions _options;
        private readonly ILogger<PayMongoClient> _logger;
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true
        };

        public PayMongoClient(HttpClient http, IOptions<PayMongoOptions> options, ILogger<PayMongoClient> logger)
        {
            _http = http;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<CheckoutSessionResponse> CreateQrPhCheckoutSessionAsync(
            Booking booking,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            if (!_options.IsConfigured)
            {
                throw new InvalidOperationException(
                    "PayMongo is not configured. Set PayMongo:SecretKey and PayMongo:WebhookSecret " +
                    "(see Documentation/PAYMONGO.md).");
            }

            // PayMongo expects integer minor units (centavos).
            var amountMinor = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

            var request = new CheckoutSessionRequest
            {
                Data = new CheckoutSessionRequestData
                {
                    Attributes = new CheckoutSessionRequestAttributes
                    {
                        CancelUrl = BuildReturnUrl(_options.CancelUrl, booking, "cancel"),
                        SuccessUrl = BuildReturnUrl(_options.SuccessUrl, booking, "success"),
                        Description = $"Giuseppe Football booking {booking.BookingReferenceNo}",
                        ReferenceNumber = booking.BookingReferenceNo,
                        LineItems = new List<CheckoutSessionLineItem>
                        {
                            new()
                            {
                                Amount = amountMinor,
                                Currency = "PHP",
                                Name = $"Booking {booking.BookingReferenceNo}",
                                Quantity = 1
                            }
                        },
                        PaymentMethodTypes = new List<string> { "qrph" },
                        Metadata = new Dictionary<string, string>
                        {
                            ["bookingId"] = booking.Id.ToString(),
                            ["bookingReference"] = booking.BookingReferenceNo,
                            ["channel"] = "giuseppe-football-aspnet-mvc"
                        }
                    }
                }
            };

            using var content = new StringContent(JsonSerializer.Serialize(request, JsonOpts),
                Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("/v1/checkout_sessions", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayMongo Checkout Session creation failed: {Status} {Body}",
                    (int)response.StatusCode, body);
                throw new InvalidOperationException(
                    $"PayMongo Checkout Session creation failed ({(int)response.StatusCode}).");
            }

            var parsed = JsonSerializer.Deserialize<CheckoutSessionResponse>(body, JsonOpts)
                ?? throw new InvalidOperationException("Empty PayMongo response.");
            return parsed;
        }

        public async Task ExpireCheckoutSessionAsync(
            string checkoutSessionId,
            CancellationToken cancellationToken = default)
        {
            if (!_options.IsConfigured)
            {
                throw new InvalidOperationException(
                    "PayMongo is not configured. Set PayMongo:SecretKey and PayMongo:WebhookSecret " +
                    "(see Documentation/PAYMONGO.md).");
            }

            if (string.IsNullOrWhiteSpace(checkoutSessionId))
            {
                throw new ArgumentException("Checkout Session ID is required.", nameof(checkoutSessionId));
            }

            using var response = await _http.PostAsync(
                $"/v1/checkout_sessions/{Uri.EscapeDataString(checkoutSessionId)}/expire",
                content: null,
                cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("PayMongo Checkout Session expiration failed: {Status} {Body}",
                    (int)response.StatusCode, body);
                throw new InvalidOperationException(
                    $"PayMongo Checkout Session expiration failed ({(int)response.StatusCode}).");
            }
        }

        private static string BuildReturnUrl(string baseUrl, Booking booking, string checkoutResult)
        {
            var separator = baseUrl.Contains('?') ? '&' : '?';
            return $"{baseUrl}{separator}reference={Uri.EscapeDataString(booking.BookingReferenceNo)}" +
                   $"&checkoutResult={Uri.EscapeDataString(checkoutResult)}";
        }

        // Helper used by Program.cs to apply the Basic-auth header at HttpClient
        // construction time. Kept here so all PayMongo HTTP wiring is in one file.
        public static AuthenticationHeaderValue BuildAuthHeader(string secretKey)
        {
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{secretKey}:"));
            return new AuthenticationHeaderValue("Basic", token);
        }
    }
}
