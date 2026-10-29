using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests
{
    [TestFixture]
    public class RssSyncServiceFixture : CoreTest<RssSyncService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IFetchAndParseRss>()
                  .Setup(s => s.Fetch())
                  .Returns(Task.FromResult(new List<ReleaseInfo>()));

            Mocker.GetMock<IPendingReleaseService>()
                  .Setup(s => s.GetPending())
                  .Returns(new List<ReleaseInfo>());

            Mocker.GetMock<IMakeDownloadDecision>()
                  .Setup(s => s.GetRssDecision(It.IsAny<List<ReleaseInfo>>()))
                  .Returns(new List<DownloadDecision>());

            Mocker.GetMock<IProcessDownloadDecisions>()
                  .Setup(s => s.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                  .Returns(Task.FromResult(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>())));
        }

        private void GivenPaused(bool paused)
        {
            Mocker.GetMock<ILegacyMigrationGate>()
                  .SetupGet(g => g.AutomationPaused)
                  .Returns(paused);
        }

        [Test]
        public void rss_sync_skipped_while_paused()
        {
            GivenPaused(true);

            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IFetchAndParseRss>().Verify(s => s.Fetch(), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<RssSyncCompleteEvent>()), Times.Never());
        }

        [Test]
        public void rss_sync_runs_when_not_paused()
        {
            GivenPaused(false);

            Subject.Execute(new RssSyncCommand());

            Mocker.GetMock<IFetchAndParseRss>().Verify(s => s.Fetch(), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<RssSyncCompleteEvent>()), Times.Once());
        }
    }
}
