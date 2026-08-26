using CourtBookingSystem.Models;

namespace CourtBookingSystem.Services
{
    public static class OperatingHoursHelper
    {
        public static (DateTime Start, DateTime End) GetOperatingRange(DateTime operatingDate, BusinessHour hours)
        {
            var start = operatingDate.Date.Add(hours.OpenTime);
            var end = operatingDate.Date.Add(hours.CloseTime);

            if (hours.CloseTime < hours.OpenTime)
            {
                end = end.AddDays(1);
            }

            return (start, end);
        }

        public static DateTime ToOperatingDateTime(DateTime operatingDate, BusinessHour hours, TimeSpan time)
        {
            var dateTime = operatingDate.Date.Add(time);

            if (hours.CloseTime < hours.OpenTime && time < hours.OpenTime)
            {
                dateTime = dateTime.AddDays(1);
            }

            return dateTime;
        }

        public static (DateTime Start, DateTime End) GetTimeWindow(DateTime operatingDate, BusinessHour hours, TimeSpan startTime, TimeSpan endTime)
        {
            var start = ToOperatingDateTime(operatingDate, hours, startTime);
            var end = ToOperatingDateTime(operatingDate, hours, endTime);

            if (end <= start)
            {
                end = end.AddDays(1);
            }

            return (start, end);
        }

        public static TimeSpan GetDuration(TimeSpan startTime, TimeSpan endTime)
        {
            var duration = endTime - startTime;

            if (duration <= TimeSpan.Zero)
            {
                duration = duration.Add(TimeSpan.FromDays(1));
            }

            return duration;
        }

        public static bool Overlaps(DateTime firstStart, DateTime firstEnd, DateTime secondStart, DateTime secondEnd) =>
            firstStart < secondEnd && firstEnd > secondStart;
    }
}
