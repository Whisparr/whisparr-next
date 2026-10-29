using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class LegacyRetryPolicyFixture : CoreTest
    {
        private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

        [TestCase(1, 5)]
        [TestCase(2, 30)]
        [TestCase(3, 120)]
        [TestCase(4, 360)]
        [TestCase(5, 360)]
        public void delays_are_5_30_120_360_minutes(int attempt, int minutes)
        {
            LegacyRetryPolicy.DelayBefore(attempt).Should().Be(TimeSpan.FromMinutes(minutes));
        }

        [TestCase(4, false)]
        [TestCase(5, true)]
        [TestCase(6, true)]
        public void gives_up_at_5(int attempts, bool expected)
        {
            LegacyRetryPolicy.ShouldGiveUp(attempts).Should().Be(expected);
        }

        [Test]
        public void an_item_that_never_failed_is_due()
        {
            LegacyRetryPolicy.IsDue(0, null, Now).Should().BeTrue();
            LegacyRetryPolicy.DueAt(0, null).Should().BeNull();
        }

        [TestCase(4, false)]
        [TestCase(5, true)]
        [TestCase(6, true)]
        public void an_item_is_due_once_its_delay_has_passed(int minutesSinceFailure, bool due)
        {
            LegacyRetryPolicy.IsDue(1, Now.AddMinutes(-minutesSinceFailure), Now).Should().Be(due);
        }

        [Test]
        public void due_time_is_the_last_attempt_plus_the_delay_for_its_attempt_count()
        {
            LegacyRetryPolicy.DueAt(3, Now).Should().Be(Now.AddHours(2));
        }

        [Test]
        public void earlier_picks_the_sooner_and_ignores_null()
        {
            LegacyRetryPolicy.Earlier(null, Now).Should().Be(Now);
            LegacyRetryPolicy.Earlier(Now, null).Should().Be(Now);
            LegacyRetryPolicy.Earlier(Now, Now.AddHours(1)).Should().Be(Now);
            LegacyRetryPolicy.Earlier(Now.AddHours(1), Now).Should().Be(Now);
        }
    }
}
