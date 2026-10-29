using System;

namespace NzbDrone.Core.LegacyMigration
{
    public static class LegacyRetryPolicy
    {
        public const int MaxAttempts = 5;

        // How long to wait before asking again while Skyhook is still resolving ids
        public static readonly TimeSpan PendingDelay = TimeSpan.FromSeconds(30);

        // How long an item may keep waiting on Skyhook's 202 (counted from the first one) before it is given up on
        public static readonly TimeSpan MaxPendingWait = TimeSpan.FromHours(24);

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
