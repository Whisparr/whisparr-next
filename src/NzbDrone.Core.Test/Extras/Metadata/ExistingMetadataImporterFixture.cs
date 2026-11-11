using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Metadata
{
    [TestFixture]
    public class ExistingMetadataImporterFixture : CoreTest<ExistingMetadataImporter>
    {
        private Movie _movie;
        private string _folder;
        private List<MetadataFile> _upserted;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\Test\Scenes\Studio".AsOsAgnostic();
            _movie = new Movie { Id = 1, Path = _folder, MovieFileId = 99 };
            _upserted = new List<MetadataFile>();

            var consumer = new Mock<IMetadata>();
            consumer.Setup(c => c.FindMetadataFile(It.IsAny<Movie>(), It.IsAny<string>()))
                    .Returns<Movie, string>((m, p) => new MetadataFile
                    {
                        MovieId = m.Id,
                        RelativePath = m.Path.GetRelativePath(p),
                        Type = MetadataType.MovieMetadata
                    });

            Mocker.SetConstant<IEnumerable<IMetadata>>(new[] { consumer.Object });

            Mocker.GetMock<IExtraFileService<MetadataFile>>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<MetadataFile>());
            Mocker.GetMock<IExtraFileService<MetadataFile>>()
                  .Setup(s => s.Upsert(It.IsAny<List<MetadataFile>>()))
                  .Callback<List<MetadataFile>>(l => _upserted.AddRange(l));

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.ParseMinimalPathMovieInfo(It.IsAny<string>()))
                  .Returns(new ParsedMovieInfo());

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(_movie)).Returns(true);
        }

        [Test]
        public void importer_uses_resolved_owner()
        {
            var mine = Path.Combine(_folder, "s1.nfo");
            var unresolved = Path.Combine(_folder, "movie.nfo");
            var owners = new Dictionary<string, int> { { mine, 11 } };

            Subject.ProcessFiles(_movie, new List<string> { mine, unresolved }, new List<string>(), null, owners).ToList();

            Mocker.GetMock<ISidecarOwnership>().Verify(v => v.FindOwner(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<List<MovieFile>>()), Times.Never());
            Mocker.GetMock<ISidecarOwnership>().Verify(v => v.GetFolderFiles(It.IsAny<Movie>()), Times.Never());
            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(11);
            _upserted[0].RelativePath.Should().Be("s1.nfo");
        }
    }
}
