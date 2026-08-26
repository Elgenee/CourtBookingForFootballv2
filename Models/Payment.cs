using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CourtBookingSystem.Models.Enums;

namespace CourtBookingSystem.Models
{
    public class Payment
    {
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [ForeignKey(nameof(BookingId))]
        public Booking? Booking { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        [Display(Name = "Payment Method")]
        public PaymentMethod PaymentMethod { get; set; }

        [Required]
        [Display(Name = "Payment Status")]
        public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

        [StringLength(100)]
        [Display(Name = "Reference No.")]
        public string? ReferenceNo { get; set; }

        [StringLength(500)]
        [Display(Name = "Proof Image Path")]
        public string? ProofImagePath { get; set; }

        [Display(Name = "Paid Date")]
        public DateTime? PaidDate { get; set; }

        // The Admin/Staff user who confirmed this payment. Nullable until confirmed.
        [Display(Name = "Confirmed By")]
        public string? ConfirmedByUserId { get; set; }

        [ForeignKey(nameof(ConfirmedByUserId))]
        public ApplicationUser? ConfirmedByUser { get; set; }

        [Display(Name = "Confirmed Date")]
        public DateTime? ConfirmedDate { get; set; }

        // ---------- Payment-gateway fields (used by PayMongo QR Ph) ----------
        // Provider tag, e.g. "PayMongo". Null for manual payments (Cash / GCash).
        [StringLength(40)]
        [Display(Name = "Payment Provider")]
        public string? PaymentProvider { get; set; }

        // PayMongo Checkout Session id / source id (the "qr reference").
        [StringLength(120)]
        [Display(Name = "QR / Session Reference")]
        public string? QrReference { get; set; }

        // PayMongo Payment id received in the webhook payload. Used for
        // idempotency — the webhook handler upserts on this column.
        [StringLength(120)]
        [Display(Name = "Gateway Transaction Id")]
        public string? GatewayTransactionId { get; set; }

        // Hosted checkout URL returned by PayMongo. We persist it so the
        // customer can return to the same QR if they navigate away.
        [StringLength(500)]
        [Display(Name = "Checkout URL")]
        public string? CheckoutUrl { get; set; }

        // Local deadline for PayMongo QR Ph checkout. Checkout Sessions stay
        // active until explicitly expired, so the app enforces this cutoff.
        [Display(Name = "Checkout Expires At")]
        public DateTime? CheckoutExpiresAtUtc { get; set; }

        // Last raw webhook payload that touched this record. Stored for
        // diagnostics + audit. Trimmed to first 8KB to stay safely under any
        // provider column limits (LONGTEXT on MySQL, NVARCHAR(MAX) elsewhere).
        [Column(TypeName = "TEXT")]
        [Display(Name = "Raw Webhook Payload")]
        public string? RawWebhookPayload { get; set; }
    }
}
