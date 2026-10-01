using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Cms;
using CourtBookingSystem.Models.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CourtBookingSystem.Services
{
    /// <summary>
    /// Server-side PDF receipt generator for booking confirmations.
    /// QuestPDF Community license is configured in Program.cs.
    /// </summary>
    public class ReceiptPdfService
    {
        private static string FormatTime(TimeSpan t) => PhilippineTime.FormatTime(t);

        public byte[] Generate(Booking booking, WebsiteSetting settings)
        {
            var payment = booking.Payments?.OrderByDescending(p => p.Id).FirstOrDefault();
            var summary = PaymentSummaryHelper.Calculate(booking);
            var isReservationOnlyPaid = booking.BookingStatus == BookingStatus.PartiallyPaid
                && summary.RemainingBalance > 0m;

            var doc = Document.Create(c =>
            {
                c.Page(p =>
                {
                    p.Size(PageSizes.A4);
                    p.Margin(40);
                    p.PageColor(Colors.White);
                    p.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Calibri));

                    p.Header().Column(col =>
                    {
                        col.Item().AlignCenter().Text(settings.WebsiteName).Bold().FontSize(20).FontColor("#0d3b66");
                        if (!string.IsNullOrEmpty(settings.WebsiteTagline))
                        {
                            col.Item().AlignCenter().Text(settings.WebsiteTagline!).FontSize(10).FontColor("#6b7280");
                        }
                        col.Item().PaddingTop(6).LineHorizontal(1).LineColor("#e6e8ee");
                    });

                    p.Content().PaddingVertical(16).Column(col =>
                    {
                        col.Spacing(14);

                        // Title
                        col.Item().AlignCenter().Text("Booking Receipt").Bold().FontSize(16);

                        // Reference
                        col.Item().Border(1).BorderColor("#c8ccd6").Padding(10).Column(refCol =>
                        {
                            refCol.Item().AlignCenter().Text("BOOKING REFERENCE").FontSize(8).LetterSpacing(1).FontColor("#6b7280");
                            refCol.Item().AlignCenter().Text(booking.BookingReferenceNo).Bold().FontSize(14).FontColor("#0d3b66");
                        });

                        // Details table
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd => { cd.RelativeColumn(2); cd.RelativeColumn(3); });
                            void Row(string label, string value)
                            {
                                t.Cell().PaddingVertical(5).Text(label).FontColor("#6b7280");
                                t.Cell().PaddingVertical(5).Text(value).Bold();
                            }
                            Row("Customer", booking.CustomerName);
                            Row("Email", booking.CustomerEmail);
                            Row("Mobile", booking.CustomerMobile);
                            Row("Field", $"{booking.Court?.CourtName} ({booking.Court?.SportType})");
                            Row("Sport", booking.Court?.SportType.ToString() ?? "-");
                            Row("Date", booking.BookingDate.ToString("dddd, MMMM d, yyyy"));
                            Row("Time", $"{FormatTime(booking.StartTime)} – {FormatTime(booking.EndTime)}");
                            Row("Booking Status", booking.BookingStatus.ToString());
                            Row("Payment Status", payment?.PaymentStatus.ToString() ?? "—");
                            Row("Payment Method", payment?.PaymentMethod.ToString() ?? "—");
                            Row("Amount Paid", summary.AmountPaid.ToString("0.00"));
                            Row("Remaining Balance", summary.RemainingBalance.ToString("0.00"));
                        });

                        col.Item().LineHorizontal(1).LineColor("#e6e8ee");

                        if (isReservationOnlyPaid)
                        {
                            col.Item().Border(1).BorderColor("#f59e0b").Background("#fffbeb").Padding(10).Column(note =>
                            {
                                note.Spacing(4);
                                note.Item().Text("Reservation payment only:").Bold().FontSize(10).FontColor("#92400e");

                                void Step(string number, string text)
                                {
                                    note.Item().PaddingLeft(10).Row(row =>
                                    {
                                        row.ConstantItem(18).Text(number).Bold().FontSize(10).FontColor("#92400e");
                                        row.RelativeItem().Text(text).FontSize(10).FontColor("#92400e");
                                    });
                                }

                                Step("1.", "Go to the website and open Find Booking.");
                                Step("2.", "Enter your booking reference and mobile number.");
                                Step("3.", "Pay the remaining balance before your schedule.");
                            });
                        }
                        else if (booking.Court?.SportType == SportType.Football
                            && booking.BookingStatus != BookingStatus.Cancelled)
                        {
                            col.Item().Border(1).BorderColor("#bfdbfe").Background("#eff6ff").Padding(10).Text(
                                "Payment confirmation of Full Payment will need to be presented before entry into play area. " +
                                "In order to avoid delays, please settle payment before your scheduled booking.")
                                .FontSize(10).FontColor("#1d4ed8");
                        }

                        // Total
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text("Total Amount").FontSize(12).FontColor("#6b7280");
                            r.ConstantItem(160).AlignRight().Text(summary.TotalAmount.ToString("0.00"))
                                .Bold().FontSize(20).FontColor("#0d3b66");
                        });
                    });

                    p.Footer().AlignCenter().Column(fcol =>
                    {
                        fcol.Item().Text("Thank you for booking with us!").FontSize(10).Italic();
                        fcol.Item().Text($"Generated {PhilippineTime.Now:MMM d, yyyy h:mm tt}").FontSize(9).FontColor("#6b7280");
                    });
                });
            });

            return doc.GeneratePdf();
        }
    }
}
