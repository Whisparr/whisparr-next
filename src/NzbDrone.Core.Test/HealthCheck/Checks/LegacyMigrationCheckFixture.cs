using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class LegacyMigrationCheckFixture : CoreTest<LegacyMigrationCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Some Message");
        }

        private void GivenState(bool completed, bool dismissed = false)
        {
            Mocker.GetMock<ILegacyMigrationService>()
                  .Setup(s => s.GetCurrent())
                  .Returns(new LegacyMigrationState { Completed = completed, ReportDismissed = dismissed });
        }

        [Test]
        public void ok_without_state()
        {
            Mocker.GetMock<ILegacyMigrationService>()
                  .Setup(s => s.GetCurrent())
                  .Returns((LegacyMigrationState)null);

            Subject.Check().Type.Should().Be(HealthCheckResult.Ok);
        }

        [Test]
        public void in_progress_notice_links_tasks()
        {
            GivenState(false);

            var result = Subject.Check();

            result.Type.Should().Be(HealthCheckResult.Notice);
            result.Reason.Should().Be(HealthCheckReason.LegacyMigrationInProgress);
            result.WikiUrl.FullUri.Should().Be("/system/tasks");
            Mocker.GetMock<ILocalizationService>()
                  .Verify(s => s.GetLocalizedString("LegacyMigrationInProgressHealthCheckMessage"), Times.Once());
        }

        [Test]
        public void completed_notice_links_report()
        {
            GivenState(true);

            var result = Subject.Check();

            result.Type.Should().Be(HealthCheckResult.Notice);
            result.Reason.Should().Be(HealthCheckReason.LegacyMigrationReport);
            result.WikiUrl.FullUri.Should().Be("/system/migration-report");
            Mocker.GetMock<ILocalizationService>()
                  .Verify(s => s.GetLocalizedString("LegacyMigrationReportHealthCheckMessage"), Times.Once());
        }

        [Test]
        public void ok_after_dismiss()
        {
            GivenState(true, true);

            Subject.Check().Type.Should().Be(HealthCheckResult.Ok);
        }
    }
}
