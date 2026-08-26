# Royal Court Booking System — Simplified Booking & Payment Statuses

## Original problem statement
Pulled from `https://github.com/Elgenee/CourtBookingSystem`. Implement the
simplified Booking Status (Pending, ForApproval, Confirmed, Completed,
Cancelled) and Payment Status (Unpaid, Submitted, Approved, Rejected)
workflow on a new feature branch `feature/royal-court-booking-enhancements`,
preserving existing functionality and matching the existing C#/ASP.NET Core
8 MVC project style.

## Stack
- ASP.NET Core 8 MVC + Razor Views
- Entity Framework Core 8 with dual provider support (SQL Server in prod,
  SQLite for cross-platform local dev — `EnsureCreated` path)
- Bootstrap 4 / AdminLTE-style CSS, custom site.css + admin.css
- QuestPDF for receipts, no JS framework

## Branch
`feature/royal-court-booking-enhancements` (off `master`, 2 commits ahead).

## What's implemented (Jan 2026 / iteration 9)
- `Models/Enums/BookingStatus.cs` reduced to 5 values; `PaymentStatus.cs`
  reduced to 4 values
- Public `Controllers/BookingsController` updated: new booking starts at
  `Pending`/`Unpaid`; proof upload moves to `ForApproval`/`Submitted`
- `Areas/Admin/Controllers/PaymentsController` rewritten — `Approve` /
  `Reject` actions; Reject bounces booking back to `Pending`
- `Areas/Staff/Controllers/PaymentsController` mirrors Admin behaviour for
  Staff users
- `Areas/Staff/Controllers/BookingsController` rewritten — actions reduced
  to **ApprovePayment**, **Complete** (manual override), **Cancel**;
  `CheckIn`, `PayAndCheckIn`, `NoShow` removed
- New `Services/BookingStatusHelper.AutoCompletePastBookingsAsync` —
  Admin/Staff dashboards & today's-list views invoke it so past Confirmed
  bookings auto-promote to Completed without a background worker
- Admin/Staff dashboards reworked — `CheckedInCount` →
  `ConfirmedTodayCount`, `PendingCheckInCount` → `PendingApprovalCount`
- New EF migration `20260602063500_SimplifyBookingPaymentStatuses` with
  portable CASE-based UPDATEs that remap legacy int values
- `Data/DbSeeder.RemapLegacyStatusesAsync` — idempotent runtime fixup
  for the SQLite `EnsureCreated` path
- Status badge CSS refreshed in both `wwwroot/css/admin.css` (Admin/Staff
  layouts) and `wwwroot/css/site.css` (public layout)
- `Documentation/WORKFLOW.md` sections C, E, F, G, H, J, M rewritten;
  `README.md` updated with Iteration 9 notes

## Verification (manual curl + sqlite3 against SQLite dev DB)
| Step | Booking Status | Payment Status | Verified |
|------|---------------|----------------|----------|
| Customer creates booking | Pending (1) | Unpaid (1) | ✓ |
| Customer uploads GCash proof | ForApproval (2) | Submitted (2) | ✓ |
| Admin approves payment | Confirmed (3) | Approved (3) | ✓ |
| Admin rejects payment | Pending (1) | Rejected (4) | ✓ |
| Staff cancels booking | Cancelled (5) | (unchanged) | ✓ |
| Build | `dotnet build` → 0 warnings, 0 errors | | ✓ |
| Boot | App boots, seeds roles + admin + courts cleanly | | ✓ |

## Files modified (25 total)
```
Areas/Admin/Controllers/DashboardController.cs      | 10 ±
Areas/Admin/Controllers/PaymentsController.cs       | 24 ±  (Verify→Approve)
Areas/Admin/Views/Payments/Index.cshtml             |  8 ±
Areas/Staff/Controllers/BookingsController.cs       | 67 ±  (rewrite)
Areas/Staff/Controllers/DashboardController.cs      | 24 ±
Areas/Staff/Controllers/PaymentsController.cs       | 18 ±
Areas/Staff/Controllers/WalkInController.cs         |  5 ±
Areas/Staff/ViewModels/StaffViewModels.cs           |  4 ±
Areas/Staff/Views/Bookings/Today.cshtml             | 32 ±  (rewrite)
Areas/Staff/Views/Dashboard/Index.cshtml            |  8 ±
Controllers/BookingsController.cs                   | 19 ±
Data/DbSeeder.cs                                    | 24 +
Documentation/WORKFLOW.md                           | 118 ±
Migrations/20260602063500_SimplifyBookingPaymentStatuses.cs         | new
Migrations/20260602063500_SimplifyBookingPaymentStatuses.Designer.cs | new
Models/Booking.cs                                   |  2 ±
Models/Enums/BookingStatus.cs                       | 15 ±
Models/Enums/PaymentStatus.cs                       | 12 ±
README.md                                           | 21 ±
Services/BookingAvailabilityService.cs              |  3 ±
Services/BookingStatusHelper.cs                     | new
Views/Bookings/Confirmation.cshtml                  |  6 ±
Views/Bookings/Receipt.cshtml                       | 15 ±
wwwroot/css/admin.css                               |  9 ±
wwwroot/css/site.css                                | 14 ±
```

## Database changes
No schema changes. Only data remapping of `Bookings.BookingStatus` and
`Payments.PaymentStatus` int values. Mapping:

| Table.Column | Old (int / name) | → | New (int / name) |
|--------------|------------------|---|------------------|
| Bookings.BookingStatus | 1 PendingPayment | → | 1 Pending |
| Bookings.BookingStatus | 2 PaymentSubmitted | → | 2 ForApproval |
| Bookings.BookingStatus | 3 Confirmed | → | 3 Confirmed |
| Bookings.BookingStatus | 4 CheckedIn | → | 3 Confirmed (merged) |
| Bookings.BookingStatus | 5 Completed | → | 4 Completed |
| Bookings.BookingStatus | 6 Cancelled | → | 5 Cancelled |
| Bookings.BookingStatus | 7 NoShow | → | 5 Cancelled (merged) |
| Payments.PaymentStatus | 1 Unpaid | → | 1 Unpaid |
| Payments.PaymentStatus | 2 Submitted | → | 2 Submitted |
| Payments.PaymentStatus | 3 Verified | → | 3 Approved (renamed) |
| Payments.PaymentStatus | 4 Rejected | → | 4 Rejected |
| Payments.PaymentStatus | 5 Refunded | → | 4 Rejected (merged) |

## Migration requirements
- **SQL Server / Postgres / MySQL deployments:** Run
  `dotnet ef database update` — applies the
  `SimplifyBookingPaymentStatuses` migration with a single transactional
  CASE UPDATE per table. The migration is idempotent for the int ranges
  involved.
- **SQLite dev (`EnsureCreated` path):** No EF migration is applied;
  instead `DbSeeder.RemapLegacyStatusesAsync` runs on every startup and
  rewrites any legacy int values it finds. Safe to run repeatedly.
- **Rollback:** `dotnet ef database update <previous_migration>` calls the
  migration's `Down()`. CheckedIn, NoShow, and Refunded are lost
  (merged statuses cannot be recovered losslessly); other values restore
  cleanly. Document this in change-management before rolling back.

## Backlog / not in scope this iteration
- Reschedule flow (mentioned in WORKFLOW.md as backlog)
- Reports area for Admin (placeholder in workflow doc)
- Background hosted service to auto-cancel old Pending bookings
- QR check-in (removed per the "no CheckedIn status" requirement, but a
  future stretch could re-introduce a transient "Arrived" UI flag without
  adding back a Booking status)

## Test credentials
Default seeded admin (unchanged from upstream):
- email: `admin@courtbooking.com`
- password: `Admin@123`

## Next action items
1. Open a PR from `feature/royal-court-booking-enhancements` → `master`
   via the Save-to-GitHub flow (or `git push origin feature/...`).
2. Run `dotnet ef database update` against the staging SQL Server before
   production deployment.
3. After merge, archive the iteration in the team changelog using the
   bullets in `README.md` Iteration 9.
