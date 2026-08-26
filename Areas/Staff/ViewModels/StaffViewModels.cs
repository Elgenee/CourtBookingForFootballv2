using System.ComponentModel.DataAnnotations;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;

namespace CourtBookingSystem.Areas.Staff.ViewModels
{
    public class StaffDashboardViewModel
    {
        public int TodayBookingsCount { get; set; }
        public int PendingApprovalCount { get; set; }
        public int ConfirmedTodayCount { get; set; }
        public decimal RevenueToday { get; set; }
        public int WalkInsToday { get; set; }
        public List<Booking> UpcomingToday { get; set; } = new();
    }

    public class WalkInFormViewModel
    {
        [Required, Display(Name = "Court")]
        public int CourtId { get; set; }

        [Required, DataType(DataType.Date), Display(Name = "Date")]
        public DateTime BookingDate { get; set; } = PhilippineTime.Today;

        [Required, DataType(DataType.Time), Display(Name = "Start Time")]
        public TimeSpan StartTime { get; set; }

        [Required, Range(1, 8), Display(Name = "Duration (hours)")]
        public int DurationHours { get; set; } = 1;

        [Required, StringLength(100), Display(Name = "Customer Name")]
        public string CustomerName { get; set; } = string.Empty;

        [Required, StringLength(20), Phone, Display(Name = "Mobile Number")]
        public string CustomerMobile { get; set; } = string.Empty;

        [EmailAddress, StringLength(150), Display(Name = "Email (optional)")]
        public string? CustomerEmail { get; set; }

        [Required, Display(Name = "Payment Method")]
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        [StringLength(500)]
        public string? Notes { get; set; }

        public List<Court> Courts { get; set; } = new();
    }
}
