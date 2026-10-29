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

        // Spread over hours, so a failure that clears within a few hours doesn't use the schedule up
        private static readonly TimeSpan[] Delays =
        {
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(6)
        };

        // Attempt 1 waits 5 minutes, 2 waits 30 minutes, 3 waits 2 hours, 4 and later wait 6 hours
        public static TimeSpan DelayBefore(int attempt)
        {
            return Delays[Math.Clamp(attempt, 1, Delays.Length) - 1];
        }

        public static bool ShouldGiveUp(int attempts)
        {
            return attempts >= MaxAttempts;
        }

        // An item that never failed is always due; otherwise it waits out the delay for its attempt count
        public static DateTime? DueAt(int attempts, DateTime? lastAttemptAt)
        {
            return attempts > 0 && lastAttemptAt.HasValue ? lastAttemptAt.Value + DelayBefore(attempts) : null;
        }

        public static bool IsDue(int attempts, DateTime? lastAttemptAt, DateTime now)
        {
            var dueAt = DueAt(attempts, lastAttemptAt);

            return dueAt == null || dueAt.Value <= now;
        }

        public static DateTime? Earlier(DateTime? current, DateTime? candidate)
        {
            if (current == null)
            {
                return candidate;
            }

            return candidate == null || current.Value <= candidate.Value ? current : candidate;
        }
    }
}
