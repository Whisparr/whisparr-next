using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ProgressMessaging
{
    [TestFixture]
    public class CommandProgressReporterFixture : CoreTest<CommandProgressReporter>
    {
        private CommandModel _command;
        private DateTime _now;

        [SetUp]
        public void Setup()
        {
            _now = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

            _command = new CommandModel
            {
                Id = 1,
                Name = "RefreshMonitoredDownloads",
                Body = new RefreshMonitoredDownloadsCommand { SendUpdatesToClient = true }
            };

            ProgressMessageContext.CommandModel = _command;
            Subject.UtcNow = () => _now;
        }

        [TearDown]
        public void TearDown()
        {
            ProgressMessageContext.CommandModel = null;
        }

        private void VerifyPublished(Times times)
        {
            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<CommandUpdatedEvent>()), times);
        }

        [Test]
        public void should_do_nothing_without_running_command()
        {
            ProgressMessageContext.CommandModel = null;

            Subject.Report("Linking files", 1, 10, 5);

            VerifyPublished(Times.Never());
        }

        [Test]
        public void should_do_nothing_when_command_does_not_send_updates()
        {
            _command.Body.SendUpdatesToClient = false;

            Subject.Report("Linking files", 1, 10, 5);

            VerifyPublished(Times.Never());
            _command.Progress.Should().BeNull();
        }

        [Test]
        public void should_store_and_publish_first_report()
        {
            Subject.Report("Linking files", 1, 10, 5);

            _command.Progress.Should().BeEquivalentTo(new CommandProgress { Phase = "Linking files", Done = 1, Total = 10, Percent = 5 });
            VerifyPublished(Times.Once());
        }

        [Test]
        public void should_throttle_reports_within_one_second()
        {
            for (var i = 1; i <= 50; i++)
            {
                Subject.Report("Linking files", i, 100, i);
            }

            VerifyPublished(Times.Once());
            _command.Progress.Done.Should().Be(50);
        }

        [Test]
        public void should_publish_again_after_one_second()
        {
            Subject.Report("Linking files", 1, 100, 1);
            _now = _now.AddSeconds(1);
            Subject.Report("Linking files", 2, 100, 2);

            VerifyPublished(Times.Exactly(2));
        }

        [Test]
        public void should_publish_on_phase_change_within_throttle()
        {
            Subject.Report("Linking files", 1, 100, 1);
            Subject.Report("Hashing files", 1, 100, 1);

            VerifyPublished(Times.Exactly(2));
        }

        [Test]
        public void should_publish_when_done_equals_total()
        {
            Subject.Report("Linking files", 1, 10, 10);
            Subject.Report("Linking files", 10, 10, 100);

            VerifyPublished(Times.Exactly(2));
        }

        [Test]
        public void should_restart_throttle_after_command_completes()
        {
            Subject.Report("Linking files", 10, 10, 100);
            Subject.Report("Linking files", 1, 10, 10);
            Subject.Report("Linking files", 2, 10, 20);

            VerifyPublished(Times.Exactly(2));
        }

        [TestCase(-5, 0)]
        [TestCase(150, 100)]
        [TestCase(double.NaN, 0)]
        public void should_clamp_percent(double percent, double expected)
        {
            Subject.Report("Linking files", 1, 10, percent);

            _command.Progress.Percent.Should().Be(expected);
        }

        [Test]
        public void should_accept_zero_total()
        {
            Action action = () => Subject.Report("Linking files", 0, 0, 0);

            action.Should().NotThrow();
        }
    }
}
