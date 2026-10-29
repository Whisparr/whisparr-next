using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadProcessingServiceFixture : CoreTest<DownloadProcessingService>
    {
        private TrackedDownload _importPending;
        private TrackedDownload _failedPending;

        [SetUp]
        public void Setup()
        {
            _importPending = new TrackedDownload
            {
                State = TrackedDownloadState.ImportPending,
                IsTrackable = true,
                DownloadItem = new DownloadClientItem { Title = "import" }
            };

            _failedPending = new TrackedDownload
            {
                State = TrackedDownloadState.FailedPending,
                IsTrackable = true,
                DownloadItem = new DownloadClientItem { Title = "failed" }
            };

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnableCompletedDownloadHandling)
                  .Returns(true);

            Mocker.GetMock<ITrackedDownloadService>()
                  .Setup(s => s.GetTrackedDownloads())
                  .Returns(new List<TrackedDownload> { _importPending, _failedPending });
        }

        private void GivenPaused(bool paused)
        {
            Mocker.GetMock<ILegacyMigrationGate>()
                  .SetupGet(g => g.AutomationPaused)
                  .Returns(paused);
        }

        [Test]
        public void import_pending_download_not_imported_while_paused()
        {
            GivenPaused(true);

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<ICompletedDownloadService>().Verify(s => s.Import(It.IsAny<TrackedDownload>()), Times.Never());
            Mocker.GetMock<IFailedDownloadService>().Verify(s => s.ProcessFailed(_failedPending), Times.Once());
            Assert.AreEqual(TrackedDownloadState.ImportPending, _importPending.State);
        }

        [Test]
        public void import_pending_download_imported_when_not_paused()
        {
            GivenPaused(false);

            Subject.Execute(new ProcessMonitoredDownloadsCommand());

            Mocker.GetMock<ICompletedDownloadService>().Verify(s => s.Import(_importPending), Times.Once());
            Mocker.GetMock<IFailedDownloadService>().Verify(s => s.ProcessFailed(_failedPending), Times.Once());
        }
    }
}
