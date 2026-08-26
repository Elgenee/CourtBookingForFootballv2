using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace CourtBookingSystem.Services.PayMongo
{
    /// <summary>
    /// Verifies the Paymongo-Signature header on inbound webhook requests.
    /// PayMongo's signature uses the same approach Stripe popularised:
    ///   t={unix_ts},te={tolerance_ts},li={1|0},te0=...,li0=...  (test/live HMACs)
    /// We compute HMAC-SHA256 over "{t}.{rawBody}" using the configured webhook
    /// secret, then constant-time compare against the value of "te" / "li".
    /// </summary>
    public interface IPayMongoWebhookVerifier
    {
        bool VerifySignature(string signatureHeader, string rawBody, out string? failureReason);
    }

    public class PayMongoWebhookVerifier : IPayMongoWebhookVerifier
    {
        private readonly PayMongoOptions _options;
        private readonly ILogger<PayMongoWebhookVerifier> _logger;
        private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

        public PayMongoWebhookVerifier(IOptions<PayMongoOptions> options, ILogger<PayMongoWebhookVerifier> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public bool VerifySignature(string signatureHeader, string rawBody, out string? failureReason)
        {
            failureReason = null;

            if (string.IsNullOrWhiteSpace(signatureHeader))
            {
                failureReason = "missing Paymongo-Signature header";
                return false;
            }

            if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
            {
                failureReason = "webhook secret not configured";
                return false;
            }

            var parts = signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in parts)
            {
                var kv = p.Split('=', 2);
                if (kv.Length == 2) dict[kv[0].Trim()] = kv[1].Trim();
            }

            if (!dict.TryGetValue("t", out var tStr) || !long.TryParse(tStr, out var ts))
            {
                failureReason = "missing or invalid t= timestamp";
                return false;
            }

            // Reject very old or future-dated signatures (replay protection).
            var when = DateTimeOffset.FromUnixTimeSeconds(ts);
            var skew = DateTimeOffset.UtcNow - when;
            if (skew.Duration() > Tolerance)
            {
                failureReason = $"timestamp out of tolerance ({skew.TotalSeconds:F0}s)";
                return false;
            }

            // PayMongo sends both "te" (test mode HMAC) and "li" (live mode HMAC).
            // Pick the one matching the configured environment.
            var sigKey = _options.IsSandbox ? "te" : "li";
            if (!dict.TryGetValue(sigKey, out var providedSig) || string.IsNullOrEmpty(providedSig))
            {
                failureReason = $"missing {sigKey}= signature component for "
                                + (_options.IsSandbox ? "sandbox" : "live") + " environment";
                return false;
            }

            var signedPayload = $"{tStr}.{rawBody}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
            var expected = Convert.ToHexString(hash).ToLowerInvariant();

            if (!FixedTimeEquals(expected, providedSig.ToLowerInvariant()))
            {
                failureReason = "signature mismatch";
                return false;
            }

            return true;
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
