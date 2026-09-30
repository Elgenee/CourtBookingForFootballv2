using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;
using CourtBookingSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CourtBookingSystem.Controllers
{
    [AllowAnonymous]
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BookingsController> _logger;
        private readonly PayMongoQrPhExpiryService _qrPhExpiry;
        private readonly FootballPaymentOptions _footballPaymentOptions;

        public BookingsController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment env,
            ILogger<BookingsController> logger,
            PayMongoQrPhExpiryService qrPhExpiry,
            IOptions<FootballPaymentOptions> footballPaymentOptions,
            CourtBookingSystem.Services.CourtGroupAvailabilityService groupAvailability,
            CourtBookingSystem.Services.BookingAvailabilityService availability,
            CourtBookingSystem.Services.BookingPricingService pricing)
        {
            _db = db;
            _userManager = userManager;
            _env = env;
            _logger = logger;
            _qrPhExpiry = qrPhExpiry;
            _footballPaymentOptions = footballPaymentOptions.Value;
            _groupAvailability = groupAvailability;
            _availability = availability;
            _pricing = pricing;
        }

        private readonly CourtBookingSystem.Services.CourtGroupAvailabilityService _groupAvailability;
        private readonly CourtBookingSystem.Services.BookingAvailabilityService _availability;
        private readonly CourtBookingSystem.Services.BookingPricingService _pricing;

        // GET /Bookings/Create?courtId=1
        // Also accepts ?sport=X&date=Y&time=Z&durationHours=N (from Quick Booking & Availability pages)
        [HttpGet]
        public async Task<IActionResult> Create(int? courtId, SportType? sport, DateTime? date, TimeSpan? time, int? durationHours)
        {
            var courts = await GetBookableCourtsAsync();
            if (courts.Count == 0)
            {
                TempData["Error"] = "No fields are currently available for booking.";
                return RedirectToAction("Index", "Home");
            }

            // If sport supplied (from the quick-booking widget) and no specific court, pick the first court of that sport.
            if (!courtId.HasValue && sport.HasValue)
            {
                var first = courts.FirstOrDefault(c => c.SportType == sport.Value);
                if (first != null) courtId = first.Id;
            }

            var vm = new BookingFormViewModel
            {
                CourtId = courtId.HasValue && courts.Any(c => c.Id == courtId.Value) ? courtId.Value : courts.First().Id,
                BookingDate = date?.Date ?? PhilippineTime.Today,
                DurationHours = durationHours ?? 1,
                Courts = courts
            };
            ViewBag.PreselectedStart = time.HasValue ? time.Value.ToString(@"hh\:mm\:ss") : "";
            if (time.HasValue) vm.StartTime = time.Value;

            // Pre-fill customer info for signed-in users
            if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    vm.CustomerName = user.FullName;
                    vm.CustomerEmail = user.Email ?? string.Empty;
                    vm.CustomerMobile = user.MobileNumber ?? string.Empty;
                }
            }

            return View(vm);
        }

        // POST /Bookings/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BookingFormViewModel vm)
        {
            // Always re-populate court list for re-display.
            vm.Courts = await GetBookableCourtsAsync();

            if (!vm.AcceptedTerms && (ModelState[nameof(vm.AcceptedTerms)]?.Errors.Count ?? 0) == 0)
            {
                ModelState.AddModelError(nameof(vm.AcceptedTerms), "Please read and agree to the Terms and Conditions before confirming your booking.");
            }

            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            if (!vm.PaymentMethod.HasValue)
            {
                ModelState.AddModelError(nameof(vm.PaymentMethod), "Please select a payment method.");
                return View(vm);
            }
            var paymentMethod = vm.PaymentMethod.Value;

            var court = vm.Courts.FirstOrDefault(c => c.Id == vm.CourtId);
            if (court == null)
            {
                ModelState.AddModelError(nameof(vm.CourtId), "Selected field is not available.");
                return View(vm);
            }

            var now = PhilippineTime.Now;

            var startTime = vm.StartTime;

            // Business hours check
            var bh = await _db.BusinessHours.FirstOrDefaultAsync(b => b.DayOfWeek == vm.BookingDate.DayOfWeek);
            if (bh == null || bh.IsClosed)
            {
                ModelState.AddModelError(nameof(vm.BookingDate), "We are closed on the selected day.");
                return View(vm);
            }

            var duration = TimeSpan.FromHours(vm.DurationHours);
            var slotStartDateTime = OperatingHoursHelper.ToOperatingDateTime(vm.BookingDate, bh, startTime);
            var slotEndDateTime = slotStartDateTime.Add(duration);
            var endTime = slotEndDateTime.TimeOfDay;
            var operatingRange = OperatingHoursHelper.GetOperatingRange(vm.BookingDate, bh);

            if (slotStartDateTime < operatingRange.Start || slotEndDateTime > operatingRange.End)
            {
                ModelState.AddModelError(nameof(vm.StartTime),
                    $"Time must be within business hours ({FormatTime(bh.OpenTime)} – {FormatTime(bh.CloseTime)}).");
                return View(vm);
            }

            // Disallow past starts using the real slot date/time, including after-midnight operating slots.
            if (slotStartDateTime < now)
            {
                ModelState.AddModelError(nameof(vm.StartTime), "Start time cannot be in the past.");
                return View(vm);
            }

            // Conflict with existing bookings (load into memory; TimeSpan comparisons
            // aren't translatable on all EF providers — same-day rows are tiny anyway).
            var sameDayBookings = await _db.Bookings
                .Where(b => b.CourtId == vm.CourtId
                    && b.BookingDate.Date == vm.BookingDate.Date
                    && b.BookingStatus != BookingStatus.Cancelled)
                .Select(b => new { b.StartTime, b.EndTime })
                .ToListAsync();
            if (sameDayBookings
                .Select(b => OperatingHoursHelper.GetTimeWindow(vm.BookingDate, bh, b.StartTime, b.EndTime))
                .Any(b => OperatingHoursHelper.Overlaps(b.Start, b.End, slotStartDateTime, slotEndDateTime)))
            {
                ModelState.AddModelError(nameof(vm.StartTime), "This time slot is already booked. Please choose another.");
                return View(vm);
            }

            // Conflict with blocked slots
            var sameDayBlocked = await _db.BlockedSlots
                .Where(s => s.CourtId == vm.CourtId && s.BlockedDate.Date == vm.BookingDate.Date)
                .Select(s => new { s.StartTime, s.EndTime })
                .ToListAsync();
            if (sameDayBlocked
                .Select(s => OperatingHoursHelper.GetTimeWindow(vm.BookingDate, bh, s.StartTime, s.EndTime))
                .Any(s => OperatingHoursHelper.Overlaps(s.Start, s.End, slotStartDateTime, slotEndDateTime)))
            {
                ModelState.AddModelError(nameof(vm.StartTime), "This time slot is unavailable.");
                return View(vm);
            }

            // Court Group / shared physical area conflict
            var groupConflicts = await _groupAvailability.GetGroupConflictsAsync(vm.CourtId, vm.BookingDate);
            var groupClash = CourtBookingSystem.Services.CourtGroupAvailabilityService
                .FindOverlap(groupConflicts, slotStartDateTime, slotEndDateTime);
            if (groupClash != null)
            {
                ModelState.AddModelError(nameof(vm.StartTime), groupClash.Reason);
                return View(vm);
            }

            // Build booking
            var price = _pricing.Quote(court, vm.BookingDate, startTime, vm.DurationHours);
            var totalAmount = price.TotalAmount;
            var referenceNo = GenerateBookingReference();

            string? userId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                var user = await _userManager.GetUserAsync(User);
                userId = user?.Id;
            }

            var booking = new Booking
            {
                BookingReferenceNo = referenceNo,
                UserId = userId,
                CustomerName = vm.CustomerName.Trim(),
                CustomerEmail = vm.CustomerEmail.Trim(),
                CustomerMobile = vm.CustomerMobile.Trim(),
                CourtId = vm.CourtId,
                BookingDate = vm.BookingDate.Date,
                StartTime = startTime,
                EndTime = endTime,
                TotalAmount = totalAmount,
                BookingStatus = BookingStatus.Pending,
                Notes = string.IsNullOrWhiteSpace(vm.Notes) ? null : vm.Notes.Trim(),
                CreatedDate = DateTime.UtcNow
            };

            var isFootballPayMongo = court.SportType == SportType.Football
                && paymentMethod == PaymentMethod.PayMongoQrPh;
            var paymentPurpose = isFootballPayMongo && vm.PayReservationFeeOnly
                ? PaymentPurpose.Reservation
                : PaymentPurpose.FullPayment;
            var initialPaymentAmount = paymentPurpose == PaymentPurpose.Reservation
                ? GetInitialReservationAmount(totalAmount)
                : totalAmount;

            var payment = new Payment
            {
                Booking = booking,
                Amount = initialPaymentAmount,
                PaymentMethod = paymentMethod,
                PaymentPurpose = paymentPurpose,
                PaymentStatus = PaymentStatus.Unpaid
            };

            _db.Bookings.Add(booking);
            _db.Payments.Add(payment);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Booking {Ref} created for field {CourtId} on {Date} {Start}-{End}",
                referenceNo, court.Id, booking.BookingDate, startTime, endTime);


            // PayMongo QR Ph: kick off the Checkout Session inline so the
            // customer can pay immediately. Manual GCash / Cash flow is
            // unchanged — those payment methods continue straight to the
            // confirmation page where the customer uploads proof (GCash) or
            // pays at the desk (Cash).
            if (paymentMethod == PaymentMethod.PayMongoQrPh)
            {
                return RedirectToAction(nameof(PayMongoStart), new { reference = referenceNo });
            }

            return RedirectToAction(nameof(Confirmation), new { reference = referenceNo });
        }

        // GET /Bookings/Find
        [HttpGet]
        public IActionResult Find()
        {
            return View(new FindBookingViewModel());
        }

        // POST /Bookings/Find
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Find(FindBookingViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                return View(vm);
            }

            var reference = vm.BookingReferenceNo.Trim().ToUpperInvariant();
            var booking = await _db.Bookings
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookingReferenceNo == reference);

            if (booking == null || NormalizeMobile(booking.CustomerMobile) != NormalizeMobile(vm.CustomerMobile))
            {
                ModelState.AddModelError(string.Empty, "We couldn't find a booking with that reference and mobile number.");
                return View(vm);
            }

            return RedirectToAction(nameof(Confirmation), new { reference = booking.BookingReferenceNo });
        }

        // POST /Bookings/StartBalancePayment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartBalancePayment(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return NotFound();

            var booking = await _db.Bookings
                .Include(b => b.Court)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingReferenceNo == reference);
            if (booking == null) return NotFound();

            if (booking.BookingStatus == BookingStatus.Cancelled)
            {
                TempData["Error"] = "This booking has been cancelled and cannot accept another payment.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (booking.BookingStatus == BookingStatus.Completed)
            {
                TempData["Error"] = "This booking has already been completed.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (booking.Court?.SportType != SportType.Football)
            {
                TempData["Error"] = "Remaining-balance PayMongo payments are available for football bookings only.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            var summary = PaymentSummaryHelper.Calculate(booking);
            if (summary.RemainingBalance <= 0m || booking.BookingStatus == BookingStatus.Confirmed)
            {
                TempData["Error"] = "This booking is already fully paid.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (booking.BookingStatus != BookingStatus.PartiallyPaid)
            {
                TempData["Error"] = "The reservation payment must be completed before paying the remaining balance.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            foreach (var existingBalance in booking.Payments
                .Where(p => p.PaymentMethod == PaymentMethod.PayMongoQrPh
                    && p.PaymentPurpose == PaymentPurpose.Balance
                    && p.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Submitted)
                .ToList())
            {
                await _qrPhExpiry.RejectBalanceIfExpiredAsync(booking, existingBalance);
            }

            var payment = booking.Payments
                .Where(p => p.PaymentMethod == PaymentMethod.PayMongoQrPh
                    && p.PaymentPurpose == PaymentPurpose.Balance
                    && p.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Submitted)
                .OrderByDescending(p => p.Id)
                .FirstOrDefault();

            if (payment == null)
            {
                payment = new Payment
                {
                    BookingId = booking.Id,
                    Amount = summary.RemainingBalance,
                    PaymentMethod = PaymentMethod.PayMongoQrPh,
                    PaymentPurpose = PaymentPurpose.Balance,
                    PaymentStatus = PaymentStatus.Unpaid
                };
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(PayMongoStart), new { reference, paymentId = payment.Id });
        }

        // GET /Bookings/PayMongoStart?reference=GFC-...
        // Creates a PayMongo QR Ph Checkout Session for the booking and
        // either redirects to the hosted QR page or shows the embedded view.
        [HttpGet]
        public async Task<IActionResult> PayMongoStart(
            string reference,
            int? paymentId,
            [FromServices] Services.PayMongo.IPayMongoClient payMongo,
            [FromServices] Microsoft.Extensions.Options.IOptions<Services.PayMongo.PayMongoOptions> payMongoOptions)
        {
            if (string.IsNullOrWhiteSpace(reference)) return NotFound();

            var booking = await _db.Bookings
                .Include(b => b.Court)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingReferenceNo == reference);
            if (booking == null) return NotFound();

            if (booking.BookingStatus == BookingStatus.Cancelled)
            {
                TempData["Error"] = "This booking was cancelled because PayMongo checkout was not completed. Please create a new booking if you still need a slot.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (booking.BookingStatus == BookingStatus.Completed)
            {
                TempData["Error"] = "This booking has already been completed.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            var payment = SelectPayMongoPayment(booking, paymentId);
            if (payment == null)
            {
                TempData["Error"] = "This booking is not a PayMongo QR Ph booking.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (payment.PaymentStatus == PaymentStatus.Approved)
            {
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (payment.PaymentPurpose == PaymentPurpose.Balance
                && await _qrPhExpiry.RejectBalanceIfExpiredAsync(booking, payment))
            {
                TempData["Error"] = "This balance payment link expired. Please start a new remaining-balance payment.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (await _qrPhExpiry.CancelIfExpiredAsync(booking, payment))
            {
                TempData["Error"] = $"This booking was cancelled because the QR Ph payment expired after {QrPhExpiryMinutes} minutes. Please create a new booking if you still need a slot.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (payment.CheckoutExpiresAtUtc == null && !string.IsNullOrWhiteSpace(payment.CheckoutUrl))
            {
                payment.CheckoutExpiresAtUtc = _qrPhExpiry.GetEffectiveExpiryUtc(booking, payment);
                await _db.SaveChangesAsync();
            }

            // If we already created a session and the customer is just
            // re-opening the page, reuse the existing checkout URL.
            if (!string.IsNullOrWhiteSpace(payment.CheckoutUrl)
                && payment.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Submitted)
            {
                ViewBag.CheckoutUrl = payment.CheckoutUrl;
                ViewBag.IsSandbox = payMongoOptions.Value.IsSandbox;
                return View("PayMongoQrPh", booking);
            }

            if (!payMongoOptions.Value.IsConfigured)
            {
                TempData["Error"] = "PayMongo is not configured on this environment. Please choose another payment method or contact support.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            try
            {
                var session = await payMongo.CreateQrPhCheckoutSessionAsync(booking, payment);

                payment.PaymentProvider = "PayMongo";
                payment.QrReference = session.Data.Id;
                payment.GatewayTransactionId = session.Data.Attributes.PaymentIntent?.Id ?? payment.GatewayTransactionId;
                payment.CheckoutUrl = session.Data.Attributes.CheckoutUrl;
                payment.CheckoutExpiresAtUtc = _qrPhExpiry.CreateExpiryUtc(DateTime.UtcNow);
                payment.ReferenceNo = session.Data.Attributes.ReferenceNumber ?? booking.BookingReferenceNo;
                payment.PaymentStatus = PaymentStatus.Unpaid;
                // status remains Unpaid until the webhook confirms payment
                await _db.SaveChangesAsync();

                ViewBag.CheckoutUrl = payment.CheckoutUrl;
                ViewBag.IsSandbox = payMongoOptions.Value.IsSandbox;
                return View("PayMongoQrPh", booking);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PayMongo session creation failed for booking {Ref}", reference);
                TempData["Error"] = "We couldn't start the PayMongo payment. Please try again, or pick another payment method.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }
        }

        // GET /Bookings/PayMongoReturn?reference=...
        // PayMongo redirects the customer here after the hosted checkout page
        // closes. Successful payment confirmation still comes from the
        // webhook; this action is informational only.
        [HttpGet]
        public IActionResult PayMongoReturn(string? reference, int? paymentId, string? checkoutResult)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return RedirectToAction(nameof(Index), "Home");
            }

            var isCancelReturn = string.Equals(checkoutResult, "cancel", StringComparison.OrdinalIgnoreCase);
            if (isCancelReturn)
            {
                return RedirectToAction(nameof(PaymentPending), new { reference, paymentId });
            }

            return RedirectToAction(nameof(Confirmation), new { reference });
        }

        // GET /Bookings/PaymentPending?reference=...
        // PayMongo cancel_url lands here when a customer leaves hosted checkout
        // with the back arrow. The booking remains reserved while webhook
        // verification catches up.
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> PaymentPending(string? reference, int? paymentId)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return RedirectToAction(nameof(Index), "Home");
            }

            var booking = await _db.Bookings
                .Include(b => b.Court)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingReferenceNo == reference);

            if (booking == null) return NotFound();

            var payment = SelectPayMongoPayment(booking, paymentId);

            if (payment == null)
            {
                TempData["Error"] = "This booking is not a PayMongo QR Ph booking.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (payment.PaymentStatus == PaymentStatus.Approved
                || booking.BookingStatus == BookingStatus.Confirmed
                || (booking.BookingStatus == BookingStatus.PartiallyPaid && payment.PaymentPurpose != PaymentPurpose.Balance))
            {
                return RedirectToAction(nameof(Receipt), new { reference });
            }

            if (payment.PaymentPurpose == PaymentPurpose.Balance
                && await _qrPhExpiry.RejectBalanceIfExpiredAsync(booking, payment))
            {
                TempData["Error"] = "This balance payment link expired. Please start a new remaining-balance payment.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (await _qrPhExpiry.CancelIfExpiredAsync(booking, payment))
            {
                TempData["Error"] = $"This booking was cancelled because the QR Ph payment expired after {QrPhExpiryMinutes} minutes. Please create a new booking if you still need a slot.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            if (payment.PaymentStatus == PaymentStatus.Unpaid
                && (booking.BookingStatus == BookingStatus.Pending || payment.PaymentPurpose == PaymentPurpose.Balance))
            {
                payment.PaymentStatus = PaymentStatus.Submitted;
                await _db.SaveChangesAsync();

                _logger.LogInformation("PayMongo checkout returned pending for booking {Ref}; awaiting webhook verification.", reference);
            }

            return View(booking);
        }

        // GET /Bookings/Confirmation/GFC-20260201-ABC123
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Confirmation(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return NotFound();

            var booking = await _db.Bookings
                .Include(b => b.Court)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingReferenceNo == reference);

            if (booking == null) return NotFound();

            var payMongoPayment = SelectActivePayMongoPayment(booking);
            if (payMongoPayment != null)
            {
                if (payMongoPayment.PaymentPurpose == PaymentPurpose.Balance)
                {
                    await _qrPhExpiry.RejectBalanceIfExpiredAsync(booking, payMongoPayment);
                }
                else
                {
                    await _qrPhExpiry.CancelIfExpiredAsync(booking, payMongoPayment);
                }
            }

            return View(booking);
        }

        // GET /Bookings/ConfirmationStatus?reference=GFC-...
        // Lightweight polling endpoint used by the confirmation page while a
        // PayMongo webhook is still settling.
        [HttpGet]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> ConfirmationStatus(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return NotFound();

            var booking = await _db.Bookings
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingReferenceNo == reference);

            if (booking == null) return NotFound();

            var payment = SelectActivePayMongoPayment(booking)
                ?? booking.Payments.OrderByDescending(p => p.Id).FirstOrDefault();

            if (payment?.PaymentMethod == PaymentMethod.PayMongoQrPh)
            {
                if (payment.PaymentPurpose == PaymentPurpose.Balance)
                {
                    await _qrPhExpiry.RejectBalanceIfExpiredAsync(booking, payment);
                }
                else
                {
                    await _qrPhExpiry.CancelIfExpiredAsync(booking, payment);
                }
            }

            var summary = PaymentSummaryHelper.Calculate(booking);

            return Json(new
            {
                bookingStatus = booking.BookingStatus.ToString(),
                paymentStatus = payment?.PaymentStatus.ToString(),
                paymentMethod = payment?.PaymentMethod.ToString(),
                paymentPurpose = payment?.PaymentPurpose.ToString(),
                amountPaid = summary.AmountPaid,
                remainingBalance = summary.RemainingBalance,
                gatewayTransactionId = payment?.GatewayTransactionId,
                paidDate = payment?.PaidDate is DateTime paidDate
                    ? PhilippineTime.FromUtc(paidDate).ToString("MMM d, yyyy h:mm tt")
                    : null,
                confirmedDate = payment?.ConfirmedDate is DateTime confirmedDate
                    ? PhilippineTime.FromUtc(confirmedDate).ToString("MMM d, yyyy h:mm tt")
                    : null
            });
        }

        // GET /Bookings/Receipt/GFC-...
        // Mobile-friendly, print-optimized standalone receipt page.
        [HttpGet]
        public async Task<IActionResult> Receipt(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return NotFound();
            var booking = await _db.Bookings
                .Include(b => b.Court)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingReferenceNo == reference);
            if (booking == null) return NotFound();
            return View(booking);
        }

        // GET /Bookings/ReceiptPdf/GFC-...
        // Returns the same receipt as a downloadable PDF (server-generated via QuestPDF).
        [HttpGet]
        public async Task<IActionResult> ReceiptPdf(
            string reference,
            [FromServices] Services.ReceiptPdfService pdfService,
            [FromServices] Services.SiteContentService content)
        {
            if (string.IsNullOrWhiteSpace(reference)) return NotFound();
            var booking = await _db.Bookings
                .Include(b => b.Court)
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingReferenceNo == reference);
            if (booking == null) return NotFound();

            var settings = await content.GetSettingsAsync();
            var bytes = pdfService.Generate(booking, settings);
            var fileName = $"Receipt-{booking.BookingReferenceNo}.pdf";
            return File(bytes, "application/pdf", fileName);
        }

        // POST /Bookings/UploadProof
        // Public — booking reference acts as the soft secret. Accepts GCash receipt images.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(6_000_000)]
        public async Task<IActionResult> UploadProof(string reference, IFormFile? proof)
        {
            if (string.IsNullOrWhiteSpace(reference)) return NotFound();

            var booking = await _db.Bookings
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.BookingReferenceNo == reference);
            if (booking == null) return NotFound();

            // Find the GCash payment on this booking
            var payment = booking.Payments
                .Where(p => p.PaymentMethod == PaymentMethod.GCash)
                .OrderByDescending(p => p.Id)
                .FirstOrDefault();
            if (payment == null)
            {
                TempData["UploadError"] = "This booking does not require a payment proof upload.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }
            if (payment.PaymentStatus == PaymentStatus.Approved)
            {
                TempData["UploadError"] = "Payment has already been approved.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            // ----- Validate file -----
            if (proof == null || proof.Length == 0)
            {
                TempData["UploadError"] = "Please choose an image file to upload.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }
            if (proof.Length > 5 * 1024 * 1024)
            {
                TempData["UploadError"] = "Image is too large. Maximum allowed is 5 MB.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            var allowedMime = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedMime.Contains(proof.ContentType, StringComparer.OrdinalIgnoreCase))
            {
                TempData["UploadError"] = "Only JPEG, PNG, or WebP images are allowed.";
                return RedirectToAction(nameof(Confirmation), new { reference });
            }

            var ext = Path.GetExtension(proof.FileName).ToLowerInvariant();
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(ext))
            {
                ext = proof.ContentType.ToLowerInvariant() switch
                {
                    "image/png" => ".png",
                    "image/webp" => ".webp",
                    _ => ".jpg"
                };
            }

            // ----- Save file -----
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var folder = Path.Combine(webRoot, "uploads", "payments");
            Directory.CreateDirectory(folder);

            var fileName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(folder, fileName);
            using (var stream = System.IO.File.Create(fullPath))
            {
                await proof.CopyToAsync(stream);
            }

            // Delete previous proof file (if customer re-uploads)
            if (!string.IsNullOrEmpty(payment.ProofImagePath))
            {
                var old = Path.Combine(webRoot, payment.ProofImagePath.Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(old))
                {
                    try { System.IO.File.Delete(old); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete old proof file {Path}", old); }
                }
            }

            payment.ProofImagePath = $"uploads/payments/{fileName}";
            payment.PaymentStatus = PaymentStatus.Submitted;
            payment.PaidDate ??= DateTime.UtcNow;

            // Customer uploaded payment proof — move booking into the
            // approval queue (covers both first-time uploads from Pending and
            // re-uploads after a Rejected verdict that reset the booking).
            if (booking.BookingStatus == BookingStatus.Pending)
            {
                booking.BookingStatus = BookingStatus.ForApproval;
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Proof uploaded for booking {Ref}: {File}", reference, fileName);

            // Notify admins/staff that this booking now has proof awaiting verification.

            TempData["UploadSuccess"] = "Payment proof uploaded! Our team will verify it shortly.";
            return RedirectToAction(nameof(Confirmation), new { reference });
        }

        // GET /Bookings/GetSlots?courtId=1&date=2026-02-01&durationHours=1
        // AJAX endpoint returning available time slots.
        // Single source of truth: delegates slot generation to
        // BookingAvailabilityService so Customer Booking, Staff Walk-In Booking
        // and any other consumer all see the same availability.
        [HttpGet]
        public async Task<IActionResult> GetSlots(int courtId, DateTime date, int durationHours = 1)
        {
            if (durationHours < 1 || durationHours > 8)
            {
                return BadRequest("Invalid duration.");
            }

            var bh = await _db.BusinessHours.FirstOrDefaultAsync(b => b.DayOfWeek == date.DayOfWeek);
            if (bh == null || bh.IsClosed)
            {
                return Json(new { closed = true, slots = Array.Empty<TimeSlotViewModel>() });
            }

            var court = await _db.Courts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == courtId);
            if (court == null) return NotFound();

            var rawSlots = await _availability.GenerateSlotsForCourtAsync(courtId, date, durationHours);
            var slots = rawSlots.Select(s =>
            {
                var price = _pricing.Quote(court, date, s.Start, durationHours);
                return new TimeSlotViewModel
                {
                    StartTime = s.Start.ToString(@"hh\:mm\:ss"),
                    EndTime = s.End.ToString(@"hh\:mm\:ss"),
                    Label = $"{FormatTime(s.Start)} – {FormatTime(s.End)}",
                    IsAvailable = s.IsAvailable,
                    Reason = s.Reason,
                    HourlyRate = price.HourlyRate,
                    TotalAmount = price.TotalAmount,
                    IsPromoRate = price.IsPromoApplied,
                    IsMixedRate = price.IsMixedRate
                };
            }).ToList();

            return Json(new { closed = false, slots });
        }

        // ------------------ helpers ------------------

        private async Task<List<Court>> GetBookableCourtsAsync()
        {
            return await _db.Courts
                .Where(c => c.IsActive && c.Status != CourtStatus.Closed && c.Status != CourtStatus.UnderMaintenance)
                .OrderBy(c => c.SportType)
                .ThenBy(c => c.CourtName)
                .ToListAsync();
        }

        private static string GenerateBookingReference()
        {
            var random = Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
            return $"GFC-{PhilippineTime.Today:yyyyMMdd}-{random}";
        }

        private decimal GetInitialReservationAmount(decimal totalAmount)
        {
            var configuredPercent = _footballPaymentOptions.ReservationFeePercent;
            if (configuredPercent <= 0m)
            {
                return totalAmount;
            }

            var reservationAmount = Math.Round(totalAmount * configuredPercent / 100m, 2, MidpointRounding.AwayFromZero);
            return Math.Min(reservationAmount, totalAmount);
        }

        private static int QrPhExpiryMinutes =>
            (int)PayMongoQrPhExpiryService.CheckoutLifetime.TotalMinutes;

        private static Payment? SelectActivePayMongoPayment(Booking booking)
        {
            return booking.Payments
                .Where(p => p.PaymentMethod == PaymentMethod.PayMongoQrPh)
                .OrderByDescending(p => p.PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.Submitted)
                .ThenByDescending(p => p.PaymentPurpose == PaymentPurpose.Balance)
                .ThenByDescending(p => p.PaymentPurpose == PaymentPurpose.Reservation)
                .ThenByDescending(p => p.Id)
                .FirstOrDefault();
        }

        private static Payment? SelectPayMongoPayment(Booking booking, int? paymentId)
        {
            if (paymentId.HasValue)
            {
                return booking.Payments.FirstOrDefault(p => p.Id == paymentId.Value
                    && p.PaymentMethod == PaymentMethod.PayMongoQrPh);
            }

            return SelectActivePayMongoPayment(booking);
        }

        private static string NormalizeMobile(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            return new string(value.Where(char.IsDigit).ToArray());
        }

        private static string FormatTime(TimeSpan t) =>
            PhilippineTime.FormatTime(t);
    }
}
