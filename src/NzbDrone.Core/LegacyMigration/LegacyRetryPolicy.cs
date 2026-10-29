using System;

namespace NzbDrone.Core.LegacyMigration
{
    public static class LegacyRetryPolicy
    {
        public const int MaxAttempts = 5;

        // Doubles from one minute: attempt 1 waits 1 minute, 2 waits 2, 3 waits 4, 4 waits 8
        public static TimeSpan DelayBefore(int attempt)
        {
            return TimeSpan.FromMinutes(Math.Pow(2, Math.Max(attempt, 1) - 1));
        }

        public static bool ShouldGiveUp(int attempts)
        {
            return attempts >= MaxAttempts;
        }
    }
}
