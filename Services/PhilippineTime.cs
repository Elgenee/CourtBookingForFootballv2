namespace CourtBookingSystem.Services
{
    public static class PhilippineTime
    {
        public const string IanaTimeZoneId = "Asia/Manila";

        public static TimeZoneInfo TimeZone { get; } = ResolveTimeZone();

        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZone);

        public static DateTime Today => Now.Date;

        public static DateTime FromUtc(DateTime utcDateTime)
        {
            var utc = utcDateTime.Kind switch
            {
                DateTimeKind.Utc => utcDateTime,
                DateTimeKind.Local => utcDateTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc)
            };

            return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZone);
        }

        public static DateTime ToUtc(DateTime philippineDateTime)
        {
            var local = DateTime.SpecifyKind(philippineDateTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, TimeZone);
        }

        public static string FormatTime(TimeSpan time) =>
            Today.Add(time).ToString("h:mm tt");

        private static TimeZoneInfo ResolveTimeZone()
        {
            foreach (var id in new[] { IanaTimeZoneId, "Singapore Standard Time", "Taipei Standard Time" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            return TimeZoneInfo.CreateCustomTimeZone("Philippine Standard Time", TimeSpan.FromHours(8), "Philippine Standard Time", "Philippine Standard Time");
        }
    }
}
