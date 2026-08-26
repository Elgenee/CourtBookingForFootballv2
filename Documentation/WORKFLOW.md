# Royal Court Booking System — End-to-End Workflow Documentation

> **Version:** 0.10 · **Last updated:** 2026-02 · **Stack:** ASP.NET Core 8 MVC + EF Core + SQL Server + Identity + Bootstrap 4 + AdminLTE 3 + jQuery

---

## A. System Overview

### Purpose
Royal Court is a court-booking platform for a multi-sport facility supporting **Pickleball, Badminton, Basketball, and Tennis**. The system allows the public to discover available courts, book online (with optional payment-proof upload), and gives staff/admin a console to manage operations end-to-end.

### Scope
- Public landing page + court browsing
- Guest booking (no account required)
- Member registration and login (ASP.NET Core Identity)
- Cash & GCash payment recording (manual verification)
- Staff console: walk-in bookings, check-in, payment verification
- Admin console: courts CRUD, business hours, blocked slots, all bookings, payment approval, notifications
- Shared notification system (Admin + Staff)
- Cross-court "Find Available Time" search

### Features (shipped)
- Landing page (hero + Quick Booking Widget + About + Gallery + Sport/Court grids)
- Find Available Time page (sport/date/duration → grouped slot results)
- Public booking + confirmation page with GCash proof upload
- Admin area: Dashboard, Bookings (filterable), Courts CRUD, Business Hours, Blocked Slots, Payments verification, Notifications
- Staff area: Dashboard, Today's Bookings, Walk-In, Payments, Notifications
- Notification bell on Admin & Staff layouts with unread badge + dropdown

### User Types
| Role | Login required | Areas accessible |
|------|----------------|------------------|
| Guest | No | Public landing, Find Availability, Bookings/Create, Bookings/Confirmation |
| Member | Yes (Identity) | Same as Guest + own bookings prefilled with profile |
| Staff | Yes (Identity, role `Staff`) | `/Staff/*` |
| Admin | Yes (Identity, role `Admin`) | `/Admin/*` AND `/Staff/*` |

---

## B. User Roles

### Guest
- **Responsibilities:** Casual visitor; books courts without an account.
- **Permissions:** Browse, search availability, create bookings, upload GCash proof.
- **Accessible pages:** `/`, `/Availability/Find`, `/Bookings/Create`, `/Bookings/Confirmation?reference=...`, `/Bookings/UploadProof`.

### Member
- **Responsibilities:** Returning customer with persistent profile.
- **Permissions:** Same as Guest, plus booking form auto-fills name/email/mobile from `ApplicationUser`.
- **Accessible pages:** All Guest pages + `/Identity/Account/Manage/*`.

### Staff
- **Responsibilities:** Operate front desk — check-ins, walk-ins, payment verification.
- **Permissions:** All Staff-area operations; view shared notifications; cannot manage courts/business-hours.
- **Accessible pages:** `/Staff/Dashboard`, `/Staff/Bookings/Today`, `/Staff/WalkIn/Create`, `/Staff/Payments`, `/Staff/Notifications`.

### Admin
- **Responsibilities:** Full system control + receives all notifications.
- **Permissions:** Everything Staff can do + Courts CRUD, BusinessHours, BlockedSlots, change any booking status, manage all bookings/payments.
- **Accessible pages:** All `/Admin/*` + `/Staff/*` (admin can use staff console).

---

## C. Guest Booking Workflow

```
[Visit landing]
      ↓
[Quick Widget: Sport + Date + Duration]
      ↓
[/Availability/Find shows all open slots across courts]
      ↓
[Click slot] → [/Bookings/Create?courtId=&date=&time= pre-filled]
      ↓
[Fill Customer Name / Email / Mobile + pick Cash or GCash]
      ↓
[POST /Bookings/Create]   ← booking ref RC-yyyyMMdd-XXXXXX generated
      ↓
[/Bookings/Confirmation?reference=...]
      ├── Cash: show desk-payment instructions; status stays Pending
      └── GCash: upload-receipt form → POST /Bookings/UploadProof
                    ↓
              [Payment status → Submitted, Booking status → ForApproval]
                    ↓
              [Admin/Staff notified — awaits approval]
                    ↓
              [Approved → Booking Confirmed / Payment Approved]
              [Rejected → Booking Pending / Payment Rejected; customer can re-upload]
```

**Inputs:** CourtId, BookingDate, StartTime, DurationHours (1–4), CustomerName, CustomerEmail, CustomerMobile, PaymentMethod, optional Notes.

**Validation:** Date ≥ today; within `BusinessHour` open/close for that DayOfWeek; no overlap with existing Bookings (excl. Cancelled); no overlap with `BlockedSlot`; not past time today.

**Status transitions for Guest flow:**
- New booking → `Pending` (Payment `Unpaid`)
- GCash proof uploaded → `ForApproval` (Payment `Submitted`)
- Admin/Staff Approve → `Confirmed` (Payment `Approved`)
- Admin/Staff Reject → Booking back to `Pending`; Payment `Rejected`; customer can re-upload
- Booking date passes → Booking auto-promoted to `Completed`

---

## D. Member Booking Workflow

```
[Register at /Identity/Account/Register]
      ↓
[Login at /Identity/Account/Login]
      ↓
[Same Find Available Times flow as Guest, but BookingsController pre-fills name/email/mobile from ApplicationUser]
      ↓
[POST /Bookings/Create — Booking.UserId = current user's Id]
      ↓
[Upload GCash proof if applicable]
      ↓
[Booking history — viewable via My Bookings (P0 backlog, not yet built)]
      ↓
[Cancellation — not yet exposed to member; currently only Admin can change status to Cancelled]
```

**Future Iteration 6** will surface `/Member/MyBookings` + profile editing + self-service cancellation.

---

## E. Staff Workflow

| Action | Page | Effect |
|--------|------|--------|
| View notifications | `/Staff/Notifications` + bell dropdown | Shared queue with Admin |
| Review today's bookings | `/Staff/Bookings/Today` (filterable by date) | List of all bookings on a given day |
| Review payment proofs | `/Staff/Payments` | Approve (cascade booking → Confirmed) or Reject |
| Manage schedules | (Admin only — Business Hours / Blocked Slots) | n/a for Staff |
| Manage court availability | (Admin only — Courts CRUD / Status) | n/a for Staff |
| Walk-in bookings | `/Staff/WalkIn/Create` | Booking auto-Confirmed; Cash auto-Approved, GCash → Submitted |
| Approve payment + confirm | `/Staff/Bookings/Today` row action | Booking → Confirmed, Payment → Approved |
| Mark complete / cancel | `/Staff/Bookings/Today` row actions | Manual override for completion or cancellation |

---

## F. Admin Workflow

| Action | Page |
|--------|------|
| Receive notifications | Bell + `/Admin/Notifications` |
| Approve booking | `/Admin/Payments` → Approve (sets Booking to Confirmed, Payment to Approved) |
| Reject booking | `/Admin/Payments` → Reject (Payment Rejected, Booking back to Pending) |
| Cancel booking | `/Admin/Bookings/Details/{id}` → ChangeStatus to Cancelled (fires notification) |
| Manage courts | `/Admin/Courts` — Create / Edit / Delete (smart soft-delete) |
| Manage calendar | `/Admin/BusinessHours` + `/Admin/BlockedSlots` |
| Manage reports | (Backlog — not yet implemented) |

---

## G. Payment Workflow (Manual GCash)

```
[Booking created — Payment Unpaid, Booking Pending]
              ↓
[Customer sends GCash off-platform to facility number]
              ↓
[Customer uploads receipt via /Bookings/UploadProof]
              ↓
[Payment Submitted, Booking ForApproval]        ← Notification fires
              ↓
[Admin or Staff opens /Admin/Payments or /Staff/Payments]
              ↓
   ┌──── Approve ─────┐                 ┌──── Reject ─────┐
   ↓                  ↓                 ↓                 ↓
Payment Approved   ConfirmedByUserId   Payment Rejected   Booking → Pending
   ↓               ConfirmedDate        ↓                 (so customer can
Booking Confirmed                       Notification        re-upload)
   ↓                                    fires
[Notification: "Booking RC-... approved"]
```

**Cash flow:** Customer pays at the counter → Staff uses the "Approve" row action on `/Staff/Bookings/Today` → Payment becomes `Approved` and Booking becomes `Confirmed` in one click. Walk-in bookings created by Staff start at `Confirmed` already, with Cash payments auto-Approved.

**PaymentStatus values (simplified):** `Unpaid → Submitted → Approved` (happy path) or `→ Rejected` (customer re-uploads, booking returns to `Pending`).

---

## H. Notification Workflow

```
[Event happens]
      ↓
[NotificationService.NotifyAsync(title, message, link, relatedBookingId)]
      ↓
[Row inserted in Notifications table — shared across all Admin + Staff users]
      ↓
[Admin & Staff layouts both mount _NotificationBell.cshtml]
      ↓
[Bell shows unread count badge; dropdown shows last 8]
      ↓
[Click notification → /Open/{id} → IsRead=true, ReadDate stamped, 302 to Link]
```

### Notification types (shipped)
| Trigger | Title | Message |
|---------|-------|---------|
| New booking | New Booking | "New booking RC-... submitted for approval." |
| Proof uploaded | Payment Proof | "Payment proof uploaded for Booking RC-..." |
| Payment approved | Booking Approved | "Booking RC-... has been approved." |
| Payment rejected | Payment Rejected | "Payment proof for booking RC-... was rejected. Customer may re-upload." |
| Status → Cancelled | Booking Cancelled | "Booking RC-... has been cancelled." |
| Reschedule | Booking Rescheduled | *(future — not implemented; reschedule flow is backlog)* |

---

## I. Calendar Workflow

### Customer-facing
- **Find Available Time** (`/Availability/Find`) — Sport + Date + Duration → AJAX results grouped by court → click slot → Bookings/Create pre-filled.
- **Booking time picker** (`/Bookings/Create`) — Loads slots for one court+date via `GET /Bookings/GetSlots` AJAX endpoint. Past slots auto-greyed; booked/blocked slots disabled.

### Admin-facing
- **Business Hours** (`/Admin/BusinessHours`) — Set open/close per `DayOfWeek` or close a day entirely.
- **Blocked Slots** (`/Admin/BlockedSlots`) — Add court+date+start+end+reason for maintenance/private events.
- **Drag-and-drop rescheduling** — *Backlog (uses FullCalendar.js when iteration ships).*
- **Court availability management** — `/Admin/Courts` toggles `Status` (Available / Reserved / UnderMaintenance / Closed) and `IsActive`.

---

## J. Booking Status Flow

The simplified workflow uses **five** booking states and **four** payment states. Earlier states like `CheckedIn`, `NoShow`, and `Refunded` have been removed in favor of a leaner Pending → ForApproval → Confirmed → Completed path (with Cancelled as the terminal opt-out).

### Booking states
| # | Name | Meaning |
|---|------|---------|
| 1 | Pending | Booking created — waiting for customer payment proof |
| 2 | ForApproval | Customer uploaded GCash payment proof — awaiting Staff/Admin review |
| 3 | Confirmed | Payment approved — booking is confirmed |
| 4 | Completed | Booking schedule finished (auto-promoted when the end time passes) |
| 5 | Cancelled | Cancelled by the customer or Staff/Admin |

### Payment states
| # | Name | Meaning |
|---|------|---------|
| 1 | Unpaid | No payment proof uploaded yet |
| 2 | Submitted | Customer uploaded payment proof |
| 3 | Approved | Payment verified by Staff/Admin |
| 4 | Rejected | Payment proof rejected — customer may re-upload |

### Transitions
```
[Created]
   │
   ▼
Pending ───(GCash proof uploaded)──► ForApproval
   │                                       │
   │                                       │ (Admin/Staff Approve payment)
   │ (Cash at counter / Walk-in)           ▼
   ├──────────────────────────────────► Confirmed
   │                                       │
   │                                       │ (Booking schedule finished — auto)
   │                                       ▼
   │                                   Completed
   │
   │ (Reject payment proof)
   ◄───── ForApproval (reverts to Pending; Payment → Rejected)
   │
   │ (Any non-terminal state, by Admin / Staff / Customer)
   ▼
Cancelled
```

**Notes:**
- `Completed` and `Cancelled` are terminal — bookings cannot return to active states.
- Conflict checks (overlap with existing booking) **exclude** `Cancelled` bookings so cancelled slots free up.
- Walk-in bookings created by Staff start at `Confirmed` (already paid at counter).
- Rejecting a payment proof bounces the booking back to `Pending` (not a separate "rejected" booking state) so the customer can simply re-upload.
- Past `Confirmed` bookings auto-promote to `Completed` whenever Admin/Staff load a dashboard or today's-bookings list (see `Services/BookingStatusHelper.cs`).

---

## K. Screen Inventory

### Public
| Screen | URL | Purpose | Main components | Actions |
|--------|-----|---------|-----------------|---------|
| Landing | `/` | Marketing + entry point | Hero, Quick Widget, About, Gallery, Sport cards, Court grid | Book Now, Find a Slot |
| Find Availability | `/Availability/Find` | Cross-court slot search | Sport/Date/Duration form, grouped result tiles | Pick slot → Bookings/Create |
| Booking Form | `/Bookings/Create` | Create booking | Court dropdown, date, slot picker, customer fields, payment method | Submit booking |
| Confirmation | `/Bookings/Confirmation?reference=` | Show ref + payment instructions | Ref box, payment-method-specific instructions, proof upload (GCash) | Upload proof, print, back home |
| Login | `/Identity/Account/Login` | Identity sign-in | Email/password form | Login |
| Register | `/Identity/Account/Register` | Member registration | Email/password + FullName | Register |

### Admin (`/Admin/*`)
| Screen | URL | Main components |
|--------|-----|-----------------|
| Dashboard | `/Admin/Dashboard` | 4 stat tiles, Recent Bookings, Sport breakdown |
| Bookings | `/Admin/Bookings` | Filters (date range / court / sport / status / search) + table |
| Booking Details | `/Admin/Bookings/Details/{id}` | Booking info, payments table, ChangeStatus form |
| Courts | `/Admin/Courts` | List + Create/Edit/Delete actions |
| Business Hours | `/Admin/BusinessHours` | Inline edit of 7 days |
| Blocked Slots | `/Admin/BlockedSlots` | Add form + list |
| Payments | `/Admin/Payments` | Status pills, thumbnail column, Verify/Reject |
| Notifications | `/Admin/Notifications` | List + Mark all read |

### Staff (`/Staff/*`)
| Screen | URL | Main components |
|--------|-----|-----------------|
| Dashboard | `/Staff/Dashboard` | 4 stat tiles + Upcoming Today + Quick Actions |
| Today's Bookings | `/Staff/Bookings/Today` | Date-filterable list + smart per-row actions |
| Walk-In | `/Staff/WalkIn/Create` | Quick form (court, time, customer, payment) |
| Payments | `/Staff/Payments` | Pending queue (Unpaid/Submitted) with thumbnail + Verify/Reject |
| Notifications | `/Staff/Notifications` | Shared notification list |

---

## L. Database Overview

### Tables
| Table | Purpose |
|-------|---------|
| `AspNetUsers` (ApplicationUser) | Identity user + FullName/MobileNumber/Address/IsActive/CreatedDate |
| `AspNetRoles` / `AspNetUserRoles` etc. | Identity role infra (`Admin`, `Staff`, `Member`) |
| `Courts` | Id, CourtName, SportType, Description, HourlyRate, Status, IsActive |
| `Bookings` | Id, BookingReferenceNo (unique), UserId?, Customer fields, CourtId, BookingDate, StartTime, EndTime, TotalAmount, BookingStatus, Notes, CreatedDate |
| `Payments` | Id, BookingId, Amount, PaymentMethod, PaymentStatus, ReferenceNo, ProofImagePath, PaidDate, ConfirmedByUserId?, ConfirmedDate? |
| `BusinessHours` | Id, DayOfWeek (unique), OpenTime, CloseTime, IsClosed |
| `BlockedSlots` | Id, CourtId, BlockedDate, StartTime, EndTime, Reason, CreatedDate |
| `Notifications` | Id, Title, Message, RelatedBookingId?, Link, IsRead, CreatedDate, ReadDate? |

### Relationships
```
ApplicationUser 1 ────* Bookings (UserId, nullable for guests)
ApplicationUser 1 ────* Payments.ConfirmedByUser (nullable)
Court           1 ────* Bookings (CourtId, Restrict on delete)
Court           1 ────* BlockedSlots (CourtId, Cascade)
Booking         1 ────* Payments (Cascade)
```

### Indexes
- `Bookings.BookingReferenceNo` — unique
- `BusinessHours.DayOfWeek` — unique

### Foreign keys
- `Booking.UserId → AspNetUsers.Id` (SetNull on delete)
- `Booking.CourtId → Courts.Id` (Restrict)
- `Payment.BookingId → Bookings.Id` (Cascade)
- `Payment.ConfirmedByUserId → AspNetUsers.Id` (SetNull)
- `BlockedSlot.CourtId → Courts.Id` (Cascade)

---

## M. Future Enhancements

| Idea | Benefit |
|------|---------|
| **Member portal (My Bookings + profile)** | Self-service repeat bookings, cancellations |
| **Membership Plans** | Recurring revenue, member discounts, priority booking windows |
| **Loyalty Points** | Earn points per booking, redeem for free hours |
| **QR Check-In** | Customer scans QR at the desk → instant Confirmed lookup, no manual search |
| **Tournament Management** | Brackets, scheduling, registration fees |
| **Mobile App** | Native iOS/Android with push notifications + Apple/Google Pay |
| **Email notifications** | Booking confirmations + reminders + receipt copies |
| **Auto-cancel hosted service** | Background job cancels Pending bookings after 2 hours, frees slot |
| **Reschedule flow** | `/Bookings/Reschedule/{ref}` with conflict check (notification trigger already named) |
| **Reports & analytics** | Revenue per sport, court utilisation, peak hours, monthly export |
| **OCR receipt auto-verify** | Run uploaded GCash image through OCR → auto-flag matching amounts green |
| **SignalR live notifications** | Real-time bell badge updates without page reload |
| **Pagination on Admin Bookings** | Currently capped at 200 rows |

---

## Business Rules (key invariants)

- Each booking has exactly one Court, one date, and a contiguous start/end TimeSpan.
- Slots are 1-hour granularity; duration 1–4 hours.
- Time-range overlap rule: two intervals overlap iff `A.Start < B.End AND A.End > B.Start`. Used everywhere for conflict checks.
- Conflict checks **exclude** Cancelled bookings so cancelled slots free up.
- Booking-date max horizon: today + 90 days (UI constraint).
- Booking reference format: `RC-yyyyMMdd-XXXXXX` (6 random uppercase hex).
- Payment proof must be JPEG / PNG / WebP ≤ 5 MB; stored under `/wwwroot/uploads/payments/{guid}.{ext}` with sanitized filenames.
- Walk-in bookings auto-generate placeholder email `walkin-{ref}@courtbook.local` if blank.
- Booking refs are unique (DB-enforced).

## Assumptions

- One facility, one timezone (server local time).
- All currency values are unitless decimals (UI prepends ₱ / treats as PHP in copy).
- GCash payment is "off-platform" — facility receives money manually; the app only stores the receipt image as proof.
- Notifications are global (shared across all admin + staff users). When any admin/staff marks read, it's read for everyone. A future iteration could make this per-user.
- Sessions are 8-hour rolling cookies (`ConfigureApplicationCookie`).

## Test Credentials (seed data)

| Role | Email | Password |
|------|-------|----------|
| Admin | `admin@courtbooking.com` | `Admin@123` |
| Staff | `staff@courtbooking.com` | `Staff@123` |

Seeded automatically on first run via `Data/DbSeeder.cs`. Passwords are config-driven (`appsettings.json` → `DefaultAdmin` / `DefaultStaff`). **Change in production.**

---

## Running Locally

```bash
cd CourtBookingSystem
dotnet restore
dotnet ef migrations add InitialCreate    # one-time
dotnet run
```

Visit `https://localhost:5001` (or `http://localhost:5000`).

For cross-platform dev preview (Linux/Mac without LocalDB), set the env var:
```bash
DbProvider=Sqlite dotnet run
```
This switches the EF provider to SQLite using a local `CourtBooking.db` file. Default remains SQL Server for production deployments.
