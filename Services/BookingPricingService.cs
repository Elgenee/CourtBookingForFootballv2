using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services
{
    public class BookingPricingService
    {
        private readonly ApplicationDbContext _db;
        private List<PricingRule>? _rules;

        public BookingPricingService(ApplicationDbContext db)
        {
            _db = db;
        }

        public record PriceQuote(
            decimal HourlyRate,
            decimal TotalAmount,
            bool IsPromoApplied,
            bool IsMixedRate);

        private record PricingRule(
            int CourtId,
            bool IsPromotional,
            DateTime? EffectiveStartDate,
            DateTime? EffectiveEndDate,
            TimeSpan StartTime,
            TimeSpan EndTime,
            decimal HourlyRate,
            int Priority,
            int DisplayOrder);

        public PriceQuote Quote(Court court, DateTime bookingDate, TimeSpan startTime, int durationHours)
        {
            if (durationHours <= 0)
            {
                return new PriceQuote(court.HourlyRate, 0m, false, false);
            }

            var courtRules = GetRules()
                .Where(r => r.CourtId == court.Id)
                .ToList();
            var bookingStart = bookingDate.Date.Add(startTime);
            var bookingEnd = bookingStart.AddHours(durationHours);
            var boundaries = GetBoundaries(courtRules, bookingStart, bookingEnd);
            var totalAmount = 0m;
            var promoApplied = false;
            var ratesUsed = new HashSet<decimal>();

            for (var i = 0; i < boundaries.Count - 1; i++)
            {
                var segmentStart = boundaries[i];
                var segmentEnd = boundaries[i + 1];
                if (segmentEnd <= segmentStart)
                {
                    continue;
                }

                var rate = GetRate(courtRules, court, segmentStart, out var isPromoRate);
                var hours = (decimal)(segmentEnd - segmentStart).TotalHours;
                totalAmount += rate * hours;
                promoApplied |= isPromoRate;
                ratesUsed.Add(rate);
            }

            totalAmount = decimal.Round(totalAmount, 2, MidpointRounding.AwayFromZero);
            var displayHourlyRate = ratesUsed.Count > 1
                ? decimal.Round(totalAmount / durationHours, 2, MidpointRounding.AwayFromZero)
                : ratesUsed.FirstOrDefault(court.HourlyRate);

            return new PriceQuote(
                displayHourlyRate,
                totalAmount,
                promoApplied,
                ratesUsed.Count > 1);
        }

        private List<PricingRule> GetRules()
        {
            _rules ??= _db.BookingPricingRules
                .AsNoTracking()
                .Where(r => r.IsEnabled)
                .OrderBy(r => r.CourtId)
                .ThenBy(r => r.DisplayOrder)
                .ThenBy(r => r.Id)
                .Select(r => new PricingRule(
                    r.CourtId,
                    r.IsPromotional,
                    r.EffectiveStartDate,
                    r.EffectiveEndDate,
                    r.StartTime,
                    r.EndTime,
                    r.HourlyRate,
                    r.Priority,
                    r.DisplayOrder))
                .ToList();

            return _rules;
        }

        private static decimal GetRate(
            IReadOnlyCollection<PricingRule> rules,
            Court court,
            DateTime segmentStart,
            out bool isPromoRate)
        {
            var promoRate = GetPromotionalRate(rules, segmentStart);
            if (promoRate.HasValue)
            {
                isPromoRate = true;
                return promoRate.Value;
            }

            isPromoRate = false;
            return GetNormalRate(rules, segmentStart) ?? court.HourlyRate;
        }

        private static decimal? GetPromotionalRate(IEnumerable<PricingRule> rules, DateTime segmentStart)
        {
            return rules
                .Where(r => r.IsPromotional
                    && r.EffectiveStartDate.GetValueOrDefault(DateTime.MinValue).Date <= segmentStart.Date
                    && r.EffectiveEndDate.GetValueOrDefault(DateTime.MaxValue).Date >= segmentStart.Date)
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.EffectiveStartDate)
                .ThenBy(r => r.DisplayOrder)
                .Select(r => IsTimeInRange(segmentStart.TimeOfDay, r.StartTime, r.EndTime)
                    ? (decimal?)r.HourlyRate
                    : null)
                .FirstOrDefault(r => r.HasValue);
        }

        private static decimal? GetNormalRate(IEnumerable<PricingRule> rules, DateTime segmentStart)
        {
            return rules
                .Where(r => !r.IsPromotional
                    && r.HourlyRate >= 0m
                    && IsTimeInRange(segmentStart.TimeOfDay, r.StartTime, r.EndTime))
                .OrderBy(r => GetRangeLength(r.StartTime, r.EndTime))
                .ThenBy(r => r.DisplayOrder)
                .Select(r => (decimal?)r.HourlyRate)
                .FirstOrDefault();
        }

        private static List<DateTime> GetBoundaries(
            IReadOnlyCollection<PricingRule> rules,
            DateTime bookingStart,
            DateTime bookingEnd)
        {
            var boundaries = new SortedSet<DateTime> { bookingStart, bookingEnd };

            for (var date = bookingStart.Date.AddDays(-1); date <= bookingEnd.Date.AddDays(1); date = date.AddDays(1))
            {
                AddBoundaryInsideQuote(boundaries, date, bookingStart, bookingEnd);

                foreach (var rate in rules)
                {
                    var rateStart = date.Add(rate.StartTime);
                    var rateEnd = date.Add(rate.EndTime);
                    if (rateEnd <= rateStart)
                    {
                        rateEnd = rateEnd.AddDays(1);
                    }

                    AddBoundaryInsideQuote(boundaries, rateStart, bookingStart, bookingEnd);
                    AddBoundaryInsideQuote(boundaries, rateEnd, bookingStart, bookingEnd);
                }
            }

            foreach (var promotion in rules.Where(r => r.IsPromotional))
            {
                if (promotion.EffectiveStartDate.HasValue)
                {
                    AddBoundaryInsideQuote(boundaries, promotion.EffectiveStartDate.Value.Date, bookingStart, bookingEnd);
                }

                if (promotion.EffectiveEndDate.HasValue)
                {
                    AddBoundaryInsideQuote(boundaries, promotion.EffectiveEndDate.Value.Date.AddDays(1), bookingStart, bookingEnd);
                }
            }

            return boundaries.ToList();
        }

        private static void AddBoundaryInsideQuote(
            ISet<DateTime> boundaries,
            DateTime boundary,
            DateTime bookingStart,
            DateTime bookingEnd)
        {
            if (boundary > bookingStart && boundary < bookingEnd)
            {
                boundaries.Add(boundary);
            }
        }

        private static bool IsTimeInRange(TimeSpan time, TimeSpan start, TimeSpan end)
        {
            if (start == end)
            {
                return true;
            }

            return start < end
                ? time >= start && time < end
                : time >= start || time < end;
        }

        private static double GetRangeLength(TimeSpan start, TimeSpan end)
        {
            var length = end - start;
            if (length <= TimeSpan.Zero)
            {
                length += TimeSpan.FromDays(1);
            }

            return length.TotalMinutes;
        }
    }
}
