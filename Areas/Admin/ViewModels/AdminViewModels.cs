using System.ComponentModel.DataAnnotations;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using CourtBookingSystem.Services;

namespace CourtBookingSystem.Areas.Admin.ViewModels
{
    public class DashboardViewModel
    {
        public int ActiveCourts { get; set; }
        public int BookingsToday { get; set; }
        public int PendingPaymentsCount { get; set; }
        public decimal RevenueThisMonth { get; set; }
        public List<Booking> RecentBookings { get; set; } = new();
        public List<KeyValuePair<SportType, int>> BookingsBySport { get; set; } = new();
    }

    public class BookingFilterViewModel
    {
        [DataType(DataType.Date)] public DateTime? FromDate { get; set; }
        [DataType(DataType.Date)] public DateTime? ToDate { get; set; }
        public int? CourtId { get; set; }
        public SportType? Sport { get; set; }
        public BookingStatus? Status { get; set; }
        public string? Search { get; set; }     // matches ref/name/email

        public List<Court> Courts { get; set; } = new();
        public List<Booking> Results { get; set; } = new();
    }

    public class BookingCalendarViewModel
    {
        public DateTime Month { get; set; }
        public DateTime PreviousMonth => Month.AddMonths(-1);
        public DateTime NextMonth => Month.AddMonths(1);
        public string MonthKey => Month.ToString("yyyy-MM");
        public int? CourtId { get; set; }
        public SportType? Sport { get; set; }
        public BookingStatus? Status { get; set; }
        public List<Court> Courts { get; set; } = new();
        public List<BookingCalendarDay> Days { get; set; } = new();
        public int BookingCount => Days.Sum(day => day.Bookings.Count);
    }

    public class BookingCalendarDay
    {
        public DateTime Date { get; set; }
        public bool IsCurrentMonth { get; set; }
        public List<Booking> Bookings { get; set; } = new();
    }

    public class CourtFormViewModel
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        [Display(Name = "Field Name")]
        public string CourtName { get; set; } = string.Empty;

        [Required, Display(Name = "Sport Type")]
        public SportType SportType { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        [Required] public CourtStatus Status { get; set; } = CourtStatus.Available;

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Field Group")]
        public int? CourtGroupId { get; set; }

        [Display(Name = "Is Full Field")]
        public bool IsFullCourt { get; set; }

        // Populated by controller for the dropdown.
        public List<CourtGroup> AvailableGroups { get; set; } = new();

        public static CourtFormViewModel From(Court c) => new()
        {
            Id = c.Id,
            CourtName = c.CourtName,
            SportType = c.SportType,
            Description = c.Description,
            Status = c.Status,
            IsActive = c.IsActive,
            CourtGroupId = c.CourtGroupId,
            IsFullCourt = c.IsFullCourt
        };
    }

    public class BusinessHoursViewModel
    {
        public List<BusinessHourRow> Days { get; set; } = new();

        public class BusinessHourRow
        {
            public int Id { get; set; }
            public DayOfWeek DayOfWeek { get; set; }
            [Required, DataType(DataType.Time)] public TimeSpan OpenTime { get; set; }
            [Required, DataType(DataType.Time)] public TimeSpan CloseTime { get; set; }
            public bool IsClosed { get; set; }
        }
    }

    public class BookingPricingIndexViewModel
    {
        public List<BookingPricingRule> Rules { get; set; } = new();
        public List<Court> Courts { get; set; } = new();
        public BookingPricingRuleFormViewModel NewRule { get; set; } = new();
    }

    public class BookingPricingRuleFormViewModel : IValidatableObject
    {
        public int Id { get; set; }

        [Required, Display(Name = "Field")]
        public int CourtId { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; } = "Normal rate";

        [Display(Name = "Promo")]
        public bool IsPromotional { get; set; }

        [Display(Name = "Enabled")]
        public bool IsEnabled { get; set; } = true;

        [DataType(DataType.Date), Display(Name = "Effective Start")]
        public DateTime? EffectiveStartDate { get; set; }

        [DataType(DataType.Date), Display(Name = "Effective End")]
        public DateTime? EffectiveEndDate { get; set; }

        [Required, DataType(DataType.Time), Display(Name = "Start Time")]
        public TimeSpan StartTime { get; set; } = new(5, 0, 0);

        [Required, DataType(DataType.Time), Display(Name = "End Time")]
        public TimeSpan EndTime { get; set; } = new(16, 0, 0);

        [Required, Range(0, 999999), Display(Name = "Hourly Rate")]
        public decimal HourlyRate { get; set; }

        [Range(0, 9999)]
        public int Priority { get; set; }

        [Display(Name = "Display Order")]
        public int DisplayOrder { get; set; }

        public List<Court> Courts { get; set; } = new();

        public static BookingPricingRuleFormViewModel From(BookingPricingRule rule) => new()
        {
            Id = rule.Id,
            CourtId = rule.CourtId,
            Name = rule.Name,
            IsPromotional = rule.IsPromotional,
            IsEnabled = rule.IsEnabled,
            EffectiveStartDate = rule.EffectiveStartDate,
            EffectiveEndDate = rule.EffectiveEndDate,
            StartTime = rule.StartTime,
            EndTime = rule.EndTime,
            HourlyRate = rule.HourlyRate,
            Priority = rule.Priority,
            DisplayOrder = rule.DisplayOrder
        };

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (StartTime == EndTime)
            {
                yield return new ValidationResult("Start and end time cannot be the same.", new[] { nameof(StartTime), nameof(EndTime) });
            }

            if (!IsPromotional)
            {
                yield break;
            }

            if (!EffectiveStartDate.HasValue)
            {
                yield return new ValidationResult("Promotional rates need an effective start date.", new[] { nameof(EffectiveStartDate) });
            }

            if (!EffectiveEndDate.HasValue)
            {
                yield return new ValidationResult("Promotional rates need an effective end date.", new[] { nameof(EffectiveEndDate) });
            }

            if (EffectiveStartDate.HasValue
                && EffectiveEndDate.HasValue
                && EffectiveEndDate.Value.Date < EffectiveStartDate.Value.Date)
            {
                yield return new ValidationResult("Effective end date must be on or after the start date.", new[] { nameof(EffectiveEndDate) });
            }
        }
    }

    public class BlockedSlotFormViewModel
    {
        [Required, Display(Name = "Field")]
        public int CourtId { get; set; }

        [Required, DataType(DataType.Date), Display(Name = "Blocked Date")]
        public DateTime BlockedDate { get; set; } = PhilippineTime.Today;

        [Required, DataType(DataType.Time), Display(Name = "Start Time")]
        public TimeSpan StartTime { get; set; } = new TimeSpan(8, 0, 0);

        [Required, DataType(DataType.Time), Display(Name = "End Time")]
        public TimeSpan EndTime { get; set; } = new TimeSpan(10, 0, 0);

        [StringLength(250)]
        public string? Reason { get; set; }

        public List<Court> Courts { get; set; } = new();
        public List<BlockedSlot> Existing { get; set; } = new();
    }

    public class CourtGroupFormViewModel
    {
        public int Id { get; set; }

        [Required, StringLength(40)]
        [RegularExpression(@"^[A-Z0-9_\-]+$", ErrorMessage = "Code must be uppercase letters, digits, underscore or dash.")]
        [Display(Name = "Group Code")]
        public string GroupCode { get; set; } = string.Empty;

        [Required, StringLength(100)]
        [Display(Name = "Group Name")]
        public string GroupName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Display(Name = "Is Active")]
        public bool IsActive { get; set; } = true;

        // Court assignment (multi-select on Create / Edit). Order matters
        // for the UI hint only — the underlying relationship is unordered.
        [Display(Name = "Member Fields")]
        public List<int> SelectedCourtIds { get; set; } = new();

        public List<Court> AvailableCourts { get; set; } = new();
        public List<Court> CurrentMembers { get; set; } = new();

        public static CourtGroupFormViewModel From(CourtGroup g) => new()
        {
            Id = g.Id,
            GroupCode = g.GroupCode,
            GroupName = g.GroupName,
            Description = g.Description,
            IsActive = g.IsActive
        };
    }
}
