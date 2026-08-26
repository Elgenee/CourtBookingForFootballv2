using CourtBookingSystem.Data;
using CourtBookingSystem.Models;
using CourtBookingSystem.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace CourtBookingSystem.Services
{
    /// <summary>
    /// Computes "shared physical field" conflicts using the configuration in
    /// <see cref="Models.CourtGroup"/> + <see cref="Models.Court.IsFullCourt"/>.
    /// The class deliberately holds NO hard-coded relationships — every rule
    /// flows from the group membership and the IsFullCourt flag.
    ///
    /// Rule recap (from the spec):
    ///   • Full Field booked  → every sibling (Full or Split) is unavailable.
    ///   • Split Field booked → only Full Field siblings become unavailable.
    ///   • Independent courts (no group) are unaffected by anyone else.
    /// </summary>
    public class CourtGroupAvailabilityService
    {
        private readonly ApplicationDbContext _db;
        public CourtGroupAvailabilityService(ApplicationDbContext db) => _db = db;

        public record GroupConflict(
            int SiblingCourtId,
            string SiblingCourtName,
            bool SiblingIsFullCourt,
            TimeSpan StartTime,
            TimeSpan EndTime,
            DateTime StartDateTime,
            DateTime EndDateTime)
        {
            public string Reason =>
                SiblingIsFullCourt
                    ? $"Unavailable — {SiblingCourtName} booking (Full Field)"
                    : $"Unavailable — shared field ({SiblingCourtName}) in use";
        }

        /// <summary>
        /// Returns all sibling-group bookings on <paramref name="date"/> that
        /// would make the given court unavailable. Empty list means the court
        /// either has no group or no overlapping sibling bookings.
        /// </summary>
        public async Task<List<GroupConflict>> GetGroupConflictsAsync(int courtId, DateTime date)
        {
            var court = await _db.Courts
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == courtId);

            if (court == null || court.CourtGroupId == null)
            {
                return new List<GroupConflict>();
            }

            // Pull every active sibling in the same group (excluding self).
            var siblings = await _db.Courts
                .AsNoTracking()
                .Where(c => c.CourtGroupId == court.CourtGroupId
                         && c.Id != courtId
                         && c.IsActive)
                .Select(c => new { c.Id, c.CourtName, c.IsFullCourt })
                .ToListAsync();

            if (siblings.Count == 0) return new List<GroupConflict>();

            // Determine which siblings can actually block this field.
            //   - If this field is a Full Field, ANY sibling booking blocks it.
            //   - If this field is a Split Field, only Full Field siblings block it.
            var blockingSiblingIds = court.IsFullCourt
                ? siblings.Select(s => s.Id).ToHashSet()
                : siblings.Where(s => s.IsFullCourt).Select(s => s.Id).ToHashSet();

            if (blockingSiblingIds.Count == 0) return new List<GroupConflict>();

            var bh = await _db.BusinessHours
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.DayOfWeek == date.DayOfWeek);
            if (bh == null) return new List<GroupConflict>();

            var nameLookup = siblings.ToDictionary(s => s.Id, s => (s.CourtName, s.IsFullCourt));

            var rows = await _db.Bookings
                .AsNoTracking()
                .Where(b => blockingSiblingIds.Contains(b.CourtId)
                    && b.BookingDate.Date == date.Date
                    && b.BookingStatus != BookingStatus.Cancelled)
                .Select(b => new { b.CourtId, b.StartTime, b.EndTime })
                .ToListAsync();

            return rows
                .Select(r =>
                {
                    var window = OperatingHoursHelper.GetTimeWindow(date, bh, r.StartTime, r.EndTime);
                    return new GroupConflict(
                        r.CourtId,
                        nameLookup[r.CourtId].CourtName,
                        nameLookup[r.CourtId].IsFullCourt,
                        r.StartTime,
                        r.EndTime,
                        window.Start,
                        window.End);
                })
                .ToList();
        }

        /// <summary>
        /// Convenience helper: returns the first group conflict that overlaps
        /// the [start, end) window, or null if none. Used to compute the
        /// "Unavailable — shared field" reason for individual slots without
        /// re-querying.
        /// </summary>
        public static GroupConflict? FindOverlap(IEnumerable<GroupConflict> conflicts, DateTime start, DateTime end)
        {
            foreach (var c in conflicts)
            {
                if (OperatingHoursHelper.Overlaps(c.StartDateTime, c.EndDateTime, start, end)) return c;
            }
            return null;
        }
    }
}
