using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class SceneDiskScanServiceFixture : CoreTest<SceneDiskScanService>
    {
        private string _root;

        [SetUp]
        public void Setup()
        {
            _root = @"C:\Test\Scenes".AsOsAgnostic();

            Mocker.GetMock<INamingConfigService>()
                  .Setup(s => s.GetConfig())
                  .Returns(new NamingConfig());

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(It.IsAny<string>()))
                  .Returns(true);
        }

        private static ImportDecision GivenDecision(string path, MatchTier tier)
        {
            return new ImportDecision(new LocalMovie { Path = path, MatchTier = tier });
        }

        [Test]
        public void should_import_hash_linked_decisions_as_a_move_and_the_rest_as_today()
        {
            var file = @"C:\Test\Scenes\import\Unknown.mp4".AsOsAgnostic();
            var hash = GivenDecision(file, MatchTier.Hash);
            var name = GivenDecision(file + "2", MatchTier.Name);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(It.IsAny<string>(), true))
                  .Returns(new[] { file });

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetSceneImportDecisions(It.IsAny<List<string>>()))
                  .Returns((new List<Movies.Movie>(), new List<MovieFile>(), new List<ImportDecision> { hash, name }));

            Subject.Scan(new List<string> { _root });

            Mocker.GetMock<IImportApprovedMovie>().Verify(
                s => s.Import(It.Is<List<ImportDecision>>(d => d.SequenceEqual(new[] { name })), false, null, ImportMode.Auto),
                Times.Once());
            Mocker.GetMock<IImportApprovedMovie>().Verify(
                s => s.Import(It.Is<List<ImportDecision>>(d => d.SequenceEqual(new[] { hash })), true, null, ImportMode.Move),
                Times.Once());
        }

        [Test]
        public void should_not_import_a_move_batch_without_hash_linked_decisions()
        {
            var file = @"C:\Test\Scenes\import\Unknown.mp4".AsOsAgnostic();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFiles(It.IsAny<string>(), true))
                  .Returns(new[] { file });

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetSceneImportDecisions(It.IsAny<List<string>>()))
                  .Returns((new List<Movies.Movie>(), new List<MovieFile>(), new List<ImportDecision> { GivenDecision(file, MatchTier.Name) }));

            Subject.Scan(new List<string> { _root });

            Mocker.GetMock<IImportApprovedMovie>().Verify(
                s => s.Import(It.IsAny<List<ImportDecision>>(), true, It.IsAny<NzbDrone.Core.Download.DownloadClientItem>(), It.IsAny<ImportMode>()),
                Times.Never());
        }
    }
}
