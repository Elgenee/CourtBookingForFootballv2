# Daily Booking Calendar

Open **Daily Calendar** in either the Admin or Staff sidebar:

- `/Admin/Bookings/DailyCalendar`
- `/Staff/Bookings/DailyCalendar`

Choose a field and a date to view the facility's configured one-hour windows. The
page shows available rates, reservation names and statuses, manual block reasons,
shared-field conflicts, and unavailable/past hours. It includes historical dates.
The previous monthly calendar remains available under **Calendar**.

Date and field changes load a shared Razor partial with GET requests. Links and
the field form also work with full page loads when JavaScript is disabled. The
endpoint inherits each area's role authorization and disables response caching.
Booked slots link to the existing booking details page in the current Admin or
Staff area. If an hour contains multiple reservations, each customer name links
to its own booking. Available, blocked, and unavailable slots remain informational.
Following a link only views the existing booking; the calendar performs no updates.

## Business rules

- `BookingAvailabilityService` supplies the existing one-hour operating windows.
- `OperatingHoursHelper` handles operating dates and midnight overlaps. Hours
  after midnight appear under the following calendar date but remain part of the
  selected operating date.
- `CourtGroupAvailabilityService` supplies shared full/split-field conflicts.
- `BookingPricingService` supplies available-hour rates using the same quoting
  convention as the existing availability page.
- All non-cancelled statuses reserve time, matching current availability rules.
  Pending reservations remain reserved until the existing workflow cancels them.
- Existing bookings/blocks take display precedence over the public `Past` reason
  so historical reservations stay visible. This page does not auto-complete them.
- Inactive, closed, and maintenance fields cannot show available hours. Closed
  days or missing hours show an explanatory empty state.
- Month cells indicate dates/today/closed days. Availability totals describe the
  selected day; per-date month totals are omitted to avoid querying every day.

The implementation introduces no schema changes, saves, or changes to public
booking, payment, validation, or conflict detection services.

## Verification

Run `dotnet run --project Tests/DailyCalendarChecks/DailyCalendarChecks.csproj`.
This dependency-free console suite creates only an in-memory SQLite database and
rejects non-SELECT SQL during calendar reads. It checks overnight and multi-hour
reservations, partial overlaps, cancellations, pricing, shared-field rules,
historical statuses, closed/inactive/maintenance fields, missing operating hours,
and endpoint authorization metadata.

Run `node Tests/DailyCalendarChecks/client-checks.cjs` to check partial loading,
stale responses, network errors/retry, field changes, and authentication redirects.
