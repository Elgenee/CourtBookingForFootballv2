# Test Credentials — Royal Court Booking System

These are seeded by `DbSeeder.SeedAsync` on first run. They are unchanged
from the upstream repository (this iteration does not modify auth).

## Admin
- Email: `admin@courtbooking.com`
- Password: `Admin@123`
- Role: `Admin`

## SuperAdmin (if seeded)
- See `appsettings.json` → `Seeding:SuperAdmin` for the configured email
  and password (env-overridable).

## Staff
- Created by Admin via `/Admin/Users`. No default Staff user is seeded.

## Member / Customer
- Public registration is **not** required for the guest booking flow —
  bookings are created with name + email + mobile. Optional account
  registration is at `/Account/Register`.
