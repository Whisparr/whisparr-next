using System.Collections.Generic;
using System.IO;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Test.Common;
using Whisparr.Api.V3.Movies;

namespace NzbDrone.Api.Test.v3.Movies
{
    [TestFixture]
    public class MovieImportControllerFixture : TestBase<MovieImportController>
    {
        private const string SceneUuid = "0b5e7c2a-1f4d-4e6a-9c3b-2d8f7a6e5c41";

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetSceneInfo(It.IsAny<string>()))
                  .Returns<string>(id => new System.Tuple<MovieMetadata, Studio, List<Performer>, List<Credit>>(
                      new MovieMetadata { ForeignId = id, Title = "Scene", ItemType = ItemType.Scene },
                      new Studio(),
                      new List<Performer>(),
                      new List<Credit>()));

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetMovieFolder(It.IsAny<Movie>(), It.IsAny<NamingConfig>()))
                  .Returns("Scene");

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildFileName(It.IsAny<Movie>(), It.IsAny<MovieFile>(), It.IsAny<NamingConfig>(), It.IsAny<List<CustomFormat>>(), It.IsAny<bool>()))
                  .Returns("Scene");

            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Returns<LocalMovie, DownloadClientItem>((l, d) => l);

            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), It.IsAny<bool>(), It.IsAny<bool>()))
                  .Returns(new List<Movie>());

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByForeignIds(It.IsAny<List<string>>()))
                  .Returns(new List<Movie>());
        }

        // A TPDB scene lookup result carries TpdbId = the scene UUID, which is not a TPDB movie id.
        [Test]
        public void should_import_tpdb_scene_by_foreign_id_not_as_tpdb_movie()
        {
            var root = Path.Combine(Path.GetTempPath(), "whisparr-import-fixture");

            Subject.Import(new List<MovieResource>
            {
                new MovieResource
                {
                    ForeignId = SceneUuid,
                    TpdbId = SceneUuid,
                    ItemType = ItemType.Scene,
                    Path = Path.Combine(root, "scene.mp4"),
                    RootFolderPath = root
                }
            });

            Mocker.GetMock<IProvideMovieInfo>().Verify(v => v.GetSceneInfo(SceneUuid), Times.Once());
            Mocker.GetMock<IProvideMovieInfo>().Verify(v => v.GetTpdbMovieInfo(It.IsAny<string>()), Times.Never());
        }
    }
}
