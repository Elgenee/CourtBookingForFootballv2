using System.Text.Json.Serialization;

namespace CourtBookingSystem.Services.PayMongo
{
    // ---- Request envelope ----
    public class CheckoutSessionRequest
    {
        [JsonPropertyName("data")] public CheckoutSessionRequestData Data { get; set; } = new();
    }

    public class CheckoutSessionRequestData
    {
        [JsonPropertyName("attributes")] public CheckoutSessionRequestAttributes Attributes { get; set; } = new();
    }

    public class CheckoutSessionRequestAttributes
    {
        [JsonPropertyName("cancel_url")] public string CancelUrl { get; set; } = string.Empty;
        [JsonPropertyName("success_url")] public string SuccessUrl { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
        [JsonPropertyName("line_items")] public List<CheckoutSessionLineItem> LineItems { get; set; } = new();
        [JsonPropertyName("payment_method_types")] public List<string> PaymentMethodTypes { get; set; } = new();
        [JsonPropertyName("metadata")] public Dictionary<string, string> Metadata { get; set; } = new();
        [JsonPropertyName("reference_number")] public string? ReferenceNumber { get; set; }
    }

    public class CheckoutSessionLineItem
    {
        [JsonPropertyName("amount")] public long Amount { get; set; }        // in centavos
        [JsonPropertyName("currency")] public string Currency { get; set; } = "PHP";
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("quantity")] public int Quantity { get; set; } = 1;
    }

    // ---- Response envelope ----
    public class CheckoutSessionResponse
    {
        [JsonPropertyName("data")] public CheckoutSessionResponseData Data { get; set; } = new();
    }

    public class CheckoutSessionResponseData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("attributes")] public CheckoutSessionResponseAttributes Attributes { get; set; } = new();
    }

    public class CheckoutSessionResponseAttributes
    {
        [JsonPropertyName("checkout_url")] public string CheckoutUrl { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("payment_intent")] public PayMongoPaymentIntent? PaymentIntent { get; set; }
        [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; set; }
        [JsonPropertyName("reference_number")] public string? ReferenceNumber { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
    }

    public class PayMongoPaymentIntent
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
    }

    // ---- Webhook payload (minimal — only the bits we touch) ----
    public class PayMongoWebhookEnvelope
    {
        [JsonPropertyName("data")] public PayMongoWebhookData Data { get; set; } = new();
    }

    public class PayMongoWebhookData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;     // event id, used for idempotency
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty; // "event"
        [JsonPropertyName("attributes")] public PayMongoWebhookEventAttributes Attributes { get; set; } = new();
    }

    public class PayMongoWebhookEventAttributes
    {
        [JsonPropertyName("type")] public string EventType { get; set; } = string.Empty;   // e.g. "payment.paid"
        [JsonPropertyName("data")] public PayMongoWebhookResource Resource { get; set; } = new();
    }

    public class PayMongoWebhookResource
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;            // payment id
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;        // "payment"
        [JsonPropertyName("attributes")] public PayMongoWebhookResourceAttributes Attributes { get; set; } = new();
    }

    public class PayMongoWebhookResourceAttributes
    {
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }                   // "paid" / "failed"
        [JsonPropertyName("paid_at")] public long? PaidAt { get; set; }
        [JsonPropertyName("source")] public PayMongoWebhookSource? Source { get; set; }
        [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; set; }
    }

    public class PayMongoWebhookSource
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
    }
}
