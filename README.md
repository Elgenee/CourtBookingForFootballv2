# Football Field Booking System — Foundation

A simple ASP.NET Core 8 MVC foundation for a Football Field Booking System supporting Football.

## Tech Stack
- ASP.NET Core 8 MVC
- Entity Framework Core 8 (SQL Server)
- ASP.NET Core Identity
- Bootstrap 4 + AdminLTE 3 (to be added in UI iteration)
- jQuery

> Architecture is deliberately simple — no Clean Architecture, CQRS, Repository, MediatR, or DDD layers.

## Project Structure
```
CourtBookingSystem/
├── CourtBookingSystem.csproj
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── Properties/
│   └── launchSettings.json
├── Models/
│   ├── ApplicationUser.cs
│   ├── Court.cs
│   ├── Booking.cs
│   ├── Payment.cs
│   ├── BusinessHour.cs
│   ├── BlockedSlot.cs
│   └── Enums/
│       ├── SportType.cs
│       ├── CourtStatus.cs
│       ├── BookingStatus.cs
│       ├── PaymentStatus.cs
│       └── PaymentMethod.cs
└── Data/
    ├── ApplicationDbContext.cs
    └── DbSeeder.cs
```

## What's Included (Iteration 11 — PayMongo QR Ph Integration)
- New PayMongo QR Ph payment method, additive to the existing Manual GCash workflow (which is **unchanged**)
- New typed `PayMongoClient` (Checkout Sessions API), `PayMongoWebhookVerifier` (constant-time HMAC-SHA256 with t / te / li scheme), and configuration via `PayMongoOptions`
- New webhook endpoint `POST /api/payments/paymongo/webhook` — verifies signature, parses event, auto-confirms booking on `payment.paid`, resets to `PendingPayment` on `payment.failed`. Idempotent via `Payment.GatewayTransactionId`
- Booking page gains a third payment option ("QR Ph (PayMongo)") with a `data-testid` selector; selecting it redirects through `/Bookings/PayMongoStart` to a new iframe-embedded QR view
- 5 new columns on `Payments` (`PaymentProvider`, `QrReference`, `GatewayTransactionId`, `CheckoutUrl`, `RawWebhookPayload`) via migration `20260616054138_AddPayMongoPaymentFields`
- Config keys: `PayMongo:SecretKey`, `PublicKey`, `WebhookSecret`, `BaseUrl`, `IsSandbox`, `SuccessUrl`, `CancelUrl` — see `Documentation/PAYMONGO.md`

## What's Included (Iteration 10 — Find Available Field Time + Expanded Notifications)
- **Find Available Field Time** page (`/Availability/Find`) — sport + date + duration form, AJAX cross-field search, grouped slot grid, click-to-book → `/Bookings/Create`
- `BookingAvailabilityService` — centralized slot logic (reused by Bookings/Create + Availability/Find)
- `Notification` model extended (`Title`, `RelatedBookingId`); EF migration `AddNotificationFields`
- Centralized `NotificationService.NotifyAsync(...)`
- **6 notification triggers** wired: new booking, proof uploaded, approved, rejected, cancelled (covers Admin + Staff)
- Generalized `_NotificationBell.cshtml` works in both Admin and Staff layouts (area-aware via RouteData)
- New `Staff/NotificationsController` + Index view + sidebar menu entry + bell in Staff top nav
- Booking reference prefix now uses **GFC-** for Giuseppe Football branding
- "Find a Slot" link in main top nav

## What's Included (Iteration 9 — Giuseppe Football Branding + About & Gallery)
- Hero title rebranded **Giuseppe Football Field**
- **About Giuseppe Football section** — description, Available Sports + Facilities cards, 4 info cards (Opening Hours / Contact / Email / Location) with placeholder values
- **Gallery section** — 8 Bootstrap-grid cards (football field views + Lounge / Reception / Training / Tournament) with placeholder `picsum.photos` images, hover lift + image zoom + magnifier overlay
- Fully mobile responsive; Facilities collapses 2-col → 1-col on mobile

## What's Included (Iteration 8 — Landing Page Redesign + Admin Notifications)
- **Premium sports-center landing page** — hero with Unsplash background, dark overlay, venue name "Giuseppe Football", tagline, Book Now CTA
- **Quick Booking Widget** — white card overlapping hero bottom: Sport / Date / Time / Search → `/Bookings/Create?sport=&date=&time=`
- Updated `BookingsController.Create` GET to accept `sport`, `date`, `time` query params with smart court pre-selection
- **Notification system** — `Notification` entity, EF migration, `NotificationsController` (Index / Open / MarkAllRead), bell partial with unread badge in admin top nav, sidebar menu entry, dropdown showing recent 8 with relative timestamps
- Triggered automatically on (1) new guest booking — "New booking submitted for approval." linking to booking details; (2) GCash proof upload — "GCash payment proof submitted for {ref}." linking to `/Admin/Payments`
- Click any notification → marks read, stamps `ReadDate`, redirects to its Link

## What's Included (Iteration 7 — GCash Proof Upload)
- `BookingsController.UploadProof` — accepts JPEG/PNG/WebP up to 5 MB, saves to `wwwroot/uploads/payments/{guid}.{ext}` with sanitized name
- Confirmation page surfaces an upload form for GCash bookings, an inline `proof-thumbnail-lg` once uploaded, with "Replace Proof" to re-submit
- On upload: `Payment.ProofImagePath` + `PaymentStatus → Submitted` + booking → `PaymentSubmitted`; re-upload removes prior file
- Admin & Staff Payments queues show a `proof-thumbnail` column that opens a shared **lightbox modal** (`Views/Shared/_ProofModal.cshtml`) with full-size image + booking reference + "Open in new tab"

## What's Included (Iteration 5 — Staff Console)
- `Areas/Staff/` — all controllers `[Authorize(Roles = "Staff,Admin")]`
- `_StaffLayout.cshtml` — AdminLTE sidebar reskinned **teal** to visually distinguish from admin blue
- **Dashboard** — 4 stat tiles (Bookings Today, Pending Check-In, Checked In, Revenue Today), Upcoming queue, Quick Actions
- **Today's Bookings** — chronological list with smart per-row action (Pay+Check-In / Check-In / Complete / No-Show)
- **Walk-In** — quick form; Cash auto-verifies, GCash starts Submitted; optional email auto-placeholder
- **Payments** — verification queue (Unpaid/Submitted), Verify (cascades booking → Confirmed) / Reject
- Default staff user seeded: `staff@giuseppefootball.com` / `Staff@123` (see `memory/test_credentials.md`)
- `_LoginPartial.cshtml` — "Staff Console" link surfaced to Staff (and Admin)

## What's Included (Iteration 4 — Admin Console)
- `Areas/Admin/` — proper ASP.NET Core area, all controllers `[Authorize(Roles = "Admin")]`
- AdminLTE 3 sidebar layout (`_AdminLayout.cshtml`) — sidebar nav, top nav with "View Site", breadcrumbs, flash alerts
- **Dashboard** — 4 stat tiles (Active Fields, Bookings Today, Pending Payments, Revenue This Month), Recent Bookings, Bookings-by-Sport
- **Bookings** — filterable list (date range, court, sport, status, search) + Details with payments table + Change Status
- **Courts** — full CRUD with smart soft/hard delete
- **Business Hours** — inline edit of all 7 days
- **Blocked Slots** — combined create form + existing-blocks table on one page
- **Payments** — filter by status pills, Verify (cascades booking → Confirmed) / Reject
- `_LoginPartial.cshtml` — "Admin Dashboard" link surfaced to Admin users
- `wwwroot/css/admin.css` — gradient stat tiles, branded sidebar, status/payment/court badge palette

## What's Included (Iteration 3 — Guest Booking Flow)
- `BookingsController` (AllowAnonymous): Create (GET/POST), GetSlots (AJAX JSON), Confirmation
- `BookingFormViewModel` + `TimeSlotViewModel`
- Slot generation respects `BusinessHour` + `BlockedSlot` + existing Bookings; past slots auto-hidden for today
- Server-side validation: past-date / out-of-hours / double-booking all rejected
- Booking reference format `GFC-yyyyMMdd-XXXXXX`
- `Views/Bookings/Create.cshtml` — 3-step form with live AJAX slot picker + sticky summary sidebar
- `Views/Bookings/Confirmation.cshtml` — green check, ref box, Cash/GCash-specific payment instructions, print button
- Home page "Book" buttons & hero CTA wired to `/Bookings/Create`

## What's Included (Iteration 2 — UI Shell)
- `HomeController` (Index = landing page with field grid, Privacy, Error)
- `Views/Shared/_Layout.cshtml` — AdminLTE 3 + Bootstrap 4 + jQuery + Font Awesome 6 (CDN)
- `Views/Shared/_LoginPartial.cshtml` — auth-aware navbar dropdown
- Landing page with hero, sports summary, field grid, CTA, footer
- `wwwroot/css/site.css` — branded styling (deep sport blue + warm yellow accent), responsive
- `wwwroot/js/site.js` — smooth scroll + nav scroll-shadow
- Optional **SQLite dev preview mode** — set env var `DbProvider=Sqlite` to run cross-platform (default stays SQL Server)

## What's Included (Iteration 1 — Foundation)
- All entity models (ApplicationUser, Court, Booking, Payment, BusinessHour, BlockedSlot)
- All enums (SportType, CourtStatus, BookingStatus, PaymentStatus, PaymentMethod)
- `ApplicationDbContext` extending `IdentityDbContext<ApplicationUser>` with relationships & indexes
- ASP.NET Core Identity registration with password policy & cookie config
- Role seeding (Admin, Staff, Member)
- Default Admin seed (`admin@giuseppefootball.com` / `Admin@123`)
- Business Hours seed (8 AM – 10 PM, every day)
- Sample field seed for Giuseppe Football
- Migration-ready (`db.Database.MigrateAsync()` runs at startup)

## Getting Started

### Prerequisites
- .NET 8 SDK — https://dotnet.microsoft.com/download
- SQL Server (LocalDB / Express / full) — the default connection string uses LocalDB
- (Optional) Visual Studio 2022 17.8+ or Rider

### 1. Restore packages
```bash
cd CourtBookingSystem
dotnet restore
```

### 2. Update the connection string (if needed)
Edit `appsettings.json`:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=CourtBookingDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"
}
```

### 3. Create the initial migration
```bash
dotnet tool install --global dotnet-ef          # one-time install
dotnet ef migrations add InitialCreate
```

### 4. Run the app (DB is created & seeded automatically on startup)
```bash
dotnet run
```

App will be available at:
- HTTP : http://localhost:5000
- HTTPS: https://localhost:5001

### Default Admin Login
| Field    | Value                       |
|----------|-----------------------------|
| Email    | `admin@giuseppefootball.com`    |
| Password | `Admin@123`                 |
| Role     | Admin                       |

## Roles
- **Admin** — Full system control
- **Staff** — Front-desk operations (check-in, payment verification)
- **Member** — Registered user who can book fields
- **Guest** — Can book without an account (Booking.UserId is nullable)

## Next Iterations (Suggested)
- Layout (`_Layout.cshtml`) with AdminLTE 3 + Bootstrap 4
- Controllers & Views for Courts, Bookings, Payments
- Public guest booking page (field availability + booking form)
- Admin dashboard (stats, manage fields, verify payments)
- Staff dashboard (today's bookings, check-in)
- Member dashboard (my bookings, my payments)
- Time-slot availability logic (respect BusinessHour + BlockedSlot + existing Bookings)
- GCash payment proof upload (file storage under `/wwwroot/uploads/payments`)
