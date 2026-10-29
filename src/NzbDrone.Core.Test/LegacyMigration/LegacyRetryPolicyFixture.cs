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
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(3, 4)]
        [TestCase(4, 8)]
        public void delays_are_1_2_4_8_minutes(int attempt, int minutes)
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
    }
}
