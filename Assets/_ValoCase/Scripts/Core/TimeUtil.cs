using System;

namespace ValoCase.Core
{
    public static class TimeUtil
    {
        public static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public static DateTime FromUnix(long unix) => DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;

        public static bool IsSameUtcDay(long unixA, long unixB)
        {
            var a = FromUnix(unixA).Date;
            var b = FromUnix(unixB).Date;
            return a == b;
        }

        public static TimeSpan TimeUntilNextUtcDay()
        {
            var now = DateTime.UtcNow;
            var next = now.Date.AddDays(1);
            return next - now;
        }
    }
}
