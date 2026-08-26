namespace CourtBookingSystem.Services.PayMongo
{
    // Bound from configuration section "PayMongo".
    // Keep secrets out of source control — use user-secrets in dev and env
    // vars (PayMongo__SecretKey etc.) in staging/production.
    public class PayMongoOptions
    {
        public string SecretKey { get; set; } = string.Empty;     // sk_test_... / sk_live_...
        public string PublicKey { get; set; } = string.Empty;     // pk_test_... / pk_live_... (not required server-side)
        public string WebhookSecret { get; set; } = string.Empty; // whsec_... — used to verify Paymongo-Signature
        public string BaseUrl { get; set; } = "https://api.paymongo.com";
        public string SuccessUrl { get; set; } = string.Empty;    // Our app URL the user is sent to after paying
        public string CancelUrl { get; set; } = string.Empty;
        public bool IsSandbox { get; set; } = true;

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(SecretKey) &&
            !string.IsNullOrWhiteSpace(WebhookSecret);
    }
}
