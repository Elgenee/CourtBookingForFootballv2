Run the boundary and HTTP integration checks from the repository root:

```powershell
dotnet run --project Tests/LateNightBookingChecks/LateNightBookingChecks.csproj
```

The checks use a disposable SQLite database and a controllable UTC clock. They cover
the inclusive 12 AM–4 AM window, exact four-hour boundaries, operating-date rollover,
customer availability counts, direct POST bypasses, and staff/admin behavior.

To also exercise the actual rendered page scripts, install `jsdom@26` and `jquery@3`
in a temporary directory, then run:

```powershell
$env:LATE_NIGHT_RENDER_DIR = Join-Path $env:TEMP 'late-night-pages'
dotnet run --project Tests/LateNightBookingChecks/LateNightBookingChecks.csproj
node Tests/LateNightBookingChecks/page-checks.cjs $env:LATE_NIGHT_RENDER_DIR <directory-containing-node_modules>
```

Page checks cover cutoff expiry while the form is open, confirmation revalidation,
selection changes, messages, counts, and starts outside the late-night window.
