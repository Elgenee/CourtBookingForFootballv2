# PayMongo QR Ph Integration Guide

> **Scope:** This document describes the PayMongo QR Ph integration added to
> Royal Court's booking system. **Manual GCash payment processing is
> deliberately untouched** — PayMongo QR Ph is an *additional* payment
> method, never a replacement.

---

## 1. Architecture

```
┌────────────────┐   1. select QR Ph    ┌──────────────────────┐
│  Customer      │ ───────────────────► │ /Bookings/Create     │
└────────────────┘                      └──────────┬───────────┘
                                                   │ 2. create Booking + Payment
                                                   ▼
                                        ┌──────────────────────┐
                                        │ /Bookings/PayMongoStart
                                        │  • POST /v1/checkout_sessions
                                        │    to api.paymongo.com
                                        └──────────┬───────────┘
                                                   │ 3. checkout_url
                                                   ▼
                                        ┌──────────────────────┐
                                        │ /Bookings/PayMongoQrPh (iframe)
                                        │  PayMongo hosts the QR code      │
                                        └──────────┬───────────┘
                                                   │ 4. customer pays in GCash/Maya/BPI/BDO/UB
                                                   ▼
                                        ┌──────────────────────┐
                                        │ POST /api/payments/  │
                                        │ paymongo/webhook     │  ◄── PayMongo
                                        │  • verify signature  │
                                        │  • flip booking/pay  │
                                        └──────────┬───────────┘
                                                   │ 5. payment.paid
                                                   ▼
                                        Booking → Confirmed
                                        Payment → Verified  (auto)
```

### Status mapping
| Event | BookingStatus | PaymentStatus |
|-------|---------------|---------------|
| Customer creates QR Ph booking | `PendingPayment` | `Unpaid` |
| Customer reaches QR page | `PendingPayment` | `Unpaid` (with `CheckoutUrl` set) |
| Webhook `payment.paid` | **`Confirmed`** | **`Verified`** |
| Webhook `payment.failed` | `PendingPayment` (reset) | `Rejected` |

PayMongo payments are **auto-confirmed** — no Staff/Admin approval required.

---

## 2. Configuration

`appsettings.json` ships with empty values:

```jsonc
"PayMongo": {
  "BaseUrl": "https://api.paymongo.com",
  "IsSandbox": true,
  "PublicKey": "",
  "SecretKey": "",
  "WebhookSecret": "",
  "SuccessUrl": "https://localhost:5001/Bookings/PayMongoReturn",
  "CancelUrl":  "https://localhost:5001/Bookings/PayMongoReturn"
}
```

**Never commit real keys.** Override with environment variables or
.NET user secrets:

```bash
# local dev
dotnet user-secrets set "PayMongo:SecretKey"     "sk_test_XXX"
dotnet user-secrets set "PayMongo:PublicKey"     "pk_test_XXX"
dotnet user-secrets set "PayMongo:WebhookSecret" "whsec_XXX"

# staging / production
export PayMongo__SecretKey="sk_live_YYY"
export PayMongo__PublicKey="pk_live_YYY"
export PayMongo__WebhookSecret="whsec_YYY"
export PayMongo__IsSandbox=false
export PayMongo__SuccessUrl="https://royalcourt.example/Bookings/PayMongoReturn"
export PayMongo__CancelUrl="https://royalcourt.example/Bookings/PayMongoReturn"
```

If `SecretKey` or `WebhookSecret` is empty:
- The "QR Ph (PayMongo)" radio button is still rendered, but
- `/Bookings/PayMongoStart` shows a friendly "PayMongo not configured —
  please pick another method" message and redirects back to the
  confirmation page. Existing GCash / Cash flows are unaffected.

### Sandbox vs Live

- **Sandbox**: `IsSandbox=true`, keys prefixed `sk_test_` / `pk_test_` / `whsec_` (test). The webhook verifier expects the `te=` signature component.
- **Live**: `IsSandbox=false`, keys prefixed `sk_live_` / `pk_live_`. The verifier expects the `li=` component.

Switching environments is a config change only — **no code change required**.

---

## 3. Webhook Configuration

In the PayMongo dashboard (https://dashboard.paymongo.com), create a webhook with:

| Field | Value |
|-------|-------|
| URL | `https://<your-domain>/api/payments/paymongo/webhook` |
| Events | `payment.paid`, `payment.failed` |

PayMongo will display the `WebhookSecret` (`whsec_…`) **only once** — save it
to `PayMongo__WebhookSecret`.

### Local development with ngrok

```bash
# 1. Start the app on localhost
dotnet run --urls "http://localhost:5099"

# 2. In another terminal, expose it
ngrok http 5099

# 3. Copy the https URL ngrok prints (e.g. https://abcd1234.ngrok.io)
#    and configure your PayMongo sandbox webhook to:
#    https://abcd1234.ngrok.io/api/payments/paymongo/webhook

# 4. Update your SuccessUrl / CancelUrl to point to the ngrok host:
export PayMongo__SuccessUrl="https://abcd1234.ngrok.io/Bookings/PayMongoReturn"
export PayMongo__CancelUrl="https://abcd1234.ngrok.io/Bookings/PayMongoReturn"
```

The ngrok URL changes every restart on the free tier; remember to update
PayMongo whenever you spin up a new tunnel.

---

## 4. Testing the Integration

### A. Successful payment (sandbox)

1. Open `/Bookings/Create`, pick a court / time
2. Select **QR Ph (PayMongo)** as the payment method
3. Submit — you'll land on `/Bookings/PayMongoStart` and see the PayMongo
   QR iframe
4. In another tab, [PayMongo sandbox docs](https://developers.paymongo.com/docs/testing)
   describe how to mark a test source as paid. (You can also fire a
   webhook manually — see below.)
5. Within seconds the page auto-redirects to the booking confirmation
   page, which now shows **Confirmed / Verified**

### B. Failed payment

Same as above, but trigger a `payment.failed` event. The booking returns
to `PendingPayment`, the payment is marked `Rejected`, and the
confirmation page shows a **Retry PayMongo payment** button.

### C. Manual webhook simulation (no real PayMongo call)

```bash
python3 - <<'PY'
import hmac, hashlib, time, json, urllib.request
SECRET = "whsec_dummy_for_local_testing"  # match PayMongo__WebhookSecret
payload = {
  "data": {
    "id": f"evt_{int(time.time())}",
    "type": "event",
    "attributes": {
      "type": "payment.paid",
      "data": {
        "id": f"pay_{int(time.time())}", "type": "payment",
        "attributes": {
          "amount": 25000, "currency": "PHP", "status": "paid",
          "paid_at": int(time.time()),
          "source": {"id": "src_qrph_test", "type": "qrph"},
          "metadata": {"bookingId": "1"}
        }
      }
    }
  }
}
raw = json.dumps(payload, separators=(",",":"))
ts = str(int(time.time()))
sig = hmac.new(SECRET.encode(), f"{ts}.{raw}".encode(), hashlib.sha256).hexdigest()
req = urllib.request.Request(
    "http://localhost:5099/api/payments/paymongo/webhook",
    data=raw.encode(),
    headers={"Content-Type":"application/json",
             "Paymongo-Signature": f"t={ts},te={sig},li=0"})
print(urllib.request.urlopen(req).read().decode())
PY
```

---

## 5. Production Deployment Checklist

- [ ] Provision real PayMongo live keys (`sk_live_` / `pk_live_` / `whsec_`)
- [ ] Set `PayMongo__IsSandbox=false`
- [ ] Update `PayMongo__SuccessUrl` and `PayMongo__CancelUrl` to your
      production hostname
- [ ] Register the production webhook URL in the PayMongo dashboard
      (events: `payment.paid`, `payment.failed`)
- [ ] Verify HTTPS is enforced (PayMongo only sends webhooks to HTTPS)
- [ ] Run `dotnet ef database update` to apply the
      `AddPayMongoPaymentFields` migration
- [ ] Smoke-test one live ₱1 payment then refund/cancel internally
- [ ] Confirm Manual GCash workflow still completes end-to-end (regression)

---

## 6. Security Notes

- **The webhook is the source of truth.** The `success_url` redirect is
  informational only — a customer who closes the wallet app before paying
  will still hit `PayMongoReturn`, but the booking stays
  `PendingPayment` until `payment.paid` arrives.
- **Signatures are verified server-side** with constant-time comparison
  (`PayMongoWebhookVerifier`). Old or future-dated signatures are
  rejected (5-minute tolerance).
- **Idempotency** is implemented via `Payment.GatewayTransactionId` —
  duplicate deliveries of the same `payment.paid` event are no-ops once
  the payment is already `Verified`.
- **Secrets** must never appear in `appsettings.json`. Use user-secrets
  for local dev and your secret manager (Azure Key Vault, AWS Secrets
  Manager, etc.) for production.
