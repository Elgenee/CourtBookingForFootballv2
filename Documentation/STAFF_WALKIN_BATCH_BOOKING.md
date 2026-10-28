# Staff walk-in batch booking

The existing `/Staff/WalkIn/Create` page now collects up to 100 slots across operating dates and fields. Staff choose a field, date, duration, and time, click **Add to Booking**, and repeat. The summary groups selections by operating date, supports removing each slot, and totals bookings, hours, and configured prices. Multiple bookings require confirmation before submission.

## Implementation and affected files

- `Areas/Staff/Controllers/WalkInController.cs`: accepts a slot collection, uses the existing availability/pricing services, validates shared-resource conflicts within the batch, creates individual Booking/Payment records, and supplies a batch results page. Older single-slot POSTs remain supported.
- `Areas/Staff/ViewModels/StaffViewModels.cs`: adds the slot collection, server-generated submission token, and server-only display/quote properties.
- `Areas/Staff/Views/WalkIn/Create.cshtml`: retains the existing layout, field/customer/payment controls, Bootstrap components, and sticky summary; adds the collection controls and safe JSON restoration after errors.
- `wwwroot/js/staff-walkin.js`: retains the four time-of-day sections and the existing GetSlots endpoint; adds selection overlap checks, grouping, totals, hidden collection inputs, request cancellation, confirmation, and repeated-submit protection.
- `Services/BookingCreationTransactionFilter.cs`: encloses availability reads and writes in one serializable transaction; returns the entered form on database or commit failure and rolls back on failure.
- `Controllers/BookingsController.cs`: adds the same transaction filter to the customer Create POST so customer and staff availability checks participate in the same concurrency protection. The customer workflow and payment behavior otherwise stay the same.
- `Areas/Staff/Views/WalkIn/Created.cshtml`: lists batch references, operating dates, midnight notes, prices, statuses, and links to existing booking details/receipt handling.
- `Tests/WalkInBookingChecks/`: real HTTP/MVC/database regression checks, a commit-failure interceptor, and JavaScript selection/DOM checks.

## Data and business rules

No database migration, new entity, or runtime package is required. Each selected slot uses the existing Booking and Payment relationships. Prices are recalculated by BookingPricingService on the server; submitted display prices are never trusted. Promotional and mixed-rate behavior, operating-date interpretation, and full/split field rules are reused.

Cash remains Confirmed with an Approved payment and the confirming staff identity. Manual GCash remains Pending with an Unpaid payment and uses existing per-booking receipt upload and verification. The existing page's payment choices remain as configured. No notification triggers were added or removed.

The unique booking-reference index also protects repeated submissions: references have the existing GFC/date shape with a 16-character suffix derived from the form token and slot index. Exact replays return existing records, including after cancellation. Only the existing integer durations of 1–8 hours are offered.

## Verification

Passed:

- Build and Razor compilation, with zero warnings/errors.
- **52 HTTP/MVC integration checks on disposable SQLite data**: single and multi-date bookings, three dates, varied durations, duplicates and overlaps, full/split resources, blocked/closed/past slots, midnight crossing and exact midnight starts, configured promo/mixed prices, Cash/GCash records, legacy POSTs, replay after cancellation, concurrent staff requests, concurrent customer/staff requests, and rollback after payment-insert and transaction-commit failures.
- **32 JavaScript checks**, including DOM interactions: add/remove, contiguous form indexes, date grouping, totals, four slot sections, highlighting, confirmation cancellation, double submission, back navigation, stale responses, and recovery with invalid restored slots.
- **33 existing daily-calendar checks**: unchanged availability, pricing, historical records, midnight handling, shared resources, read-only behavior, and endpoint authorization.
- `git diff --check` and JavaScript syntax checks.

Run the dependency-free checks from the repository root:

```powershell
dotnet run --project Tests/WalkInBookingChecks/WalkInBookingChecks.csproj
node Tests/WalkInBookingChecks/selection-checks.cjs
dotnet run --project Tests/DailyCalendarChecks/DailyCalendarChecks.csproj
```

The Node command alone runs 15 selection checks. To include the DOM checks without adding application dependencies, install jsdom and jQuery in a temporary folder:

```powershell
$walkInDeps = Join-Path $env:TEMP 'codex-walkin-dom-checks'
npm install --prefix $walkInDeps --ignore-scripts --no-audit --no-fund jsdom@16.7.0 jquery@3.6.4
$env:WALKIN_DOM_MODULES = Join-Path $walkInDeps 'node_modules'
node Tests/WalkInBookingChecks/selection-checks.cjs
```

An optional SQL Server run uses a uniquely named disposable LocalDB database:

```powershell
dotnet run --project Tests/WalkInBookingChecks/WalkInBookingChecks.csproj -- --sqlserver
```

## Limits and remaining validation

The SQL Server attempt timed out while creating its disposable database, before booking checks ran. SQL Server locking behavior therefore still needs an integration run in an available SQL Server environment. Concurrent contention may return a retryable validation page rather than silently retrying a transaction. The entered selections remain available for review.

UI behavior was exercised in a DOM environment and Razor was rendered through HTTP. A visual check in desktop and mobile browsers remains advisable; no browser screenshot review was performed.

Shared availability continues to interpret persisted bookings/blocks using the application's existing operating-date rules. Existing same-operating-date policies and pricing semantics were preserved.
