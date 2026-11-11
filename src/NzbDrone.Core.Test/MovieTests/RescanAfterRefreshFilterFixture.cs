using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class RescanAfterRefreshFilterFixture : CoreTest<RescanAfterRefreshFilter>
    {
        private readonly Movie _movie = new Movie { Id = 1 };

        private void GivenSetting(RescanAfterRefreshType type)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.RescanAfterRefresh).Returns(type);
        }

        private void VerifySkipEvent(MovieScanSkippedReason reason, Times times)
        {
            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<MovieScanSkippedEvent>(e => e.Movie == _movie && e.Reason == reason)), times);
        }

        [Test]
        public void new_item_is_rescanned_even_when_never()
        {
            GivenSetting(RescanAfterRefreshType.Never);

            Subject.ShouldRescan(_movie, true, CommandTrigger.Unspecified).Should().BeTrue();

            VerifySkipEvent(MovieScanSkippedReason.NeverRescanAfterRefresh, Times.Never());
        }

        [Test]
        public void never_skips_with_event()
        {
            GivenSetting(RescanAfterRefreshType.Never);

            Subject.ShouldRescan(_movie, false, CommandTrigger.Manual).Should().BeFalse();

            VerifySkipEvent(MovieScanSkippedReason.NeverRescanAfterRefresh, Times.Once());
        }

        [Test]
        public void after_manual_skips_automatic_trigger()
        {
            GivenSetting(RescanAfterRefreshType.AfterManual);

            Subject.ShouldRescan(_movie, false, CommandTrigger.Unspecified).Should().BeFalse();

            VerifySkipEvent(MovieScanSkippedReason.RescanAfterManualRefreshOnly, Times.Once());
        }

        [Test]
        public void after_manual_rescans_manual_trigger()
        {
            GivenSetting(RescanAfterRefreshType.AfterManual);

            Subject.ShouldRescan(_movie, false, CommandTrigger.Manual).Should().BeTrue();
        }
    }
}
