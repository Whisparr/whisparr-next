using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Subtitles
{
    [TestFixture]
    public class ExistingSubtitleImporterFixture : CoreTest<ExistingSubtitleImporter>
    {
        private Movie _movie;
        private string _folder;
        private List<SubtitleFile> _upserted;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\Test\Scenes\Studio".AsOsAgnostic();
            _movie = new Movie { Id = 1, Path = _folder, MovieFileId = 99 };
            _upserted = new List<SubtitleFile>();

            Mocker.GetMock<IExtraFileService<SubtitleFile>>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<SubtitleFile>());
            Mocker.GetMock<IExtraFileService<SubtitleFile>>()
                  .Setup(s => s.Upsert(It.IsAny<List<SubtitleFile>>()))
                  .Callback<List<SubtitleFile>>(l => _upserted.AddRange(l));

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.ParseMinimalPathMovieInfo(It.IsAny<string>()))
                  .Returns(new ParsedMovieInfo());
        }

        [Test]
        public void importer_uses_resolved_owner()
        {
            var mine = Path.Combine(_folder, "s1.en.srt");
            var unresolved = Path.Combine(_folder, "other.en.srt");
            var owners = new Dictionary<string, int> { { mine, 11 } };

            Subject.ProcessFiles(_movie, new List<string> { mine, unresolved }, new List<string>(), null, owners).ToList();

            Mocker.GetMock<ISidecarOwnership>().Verify(v => v.FindOwner(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<List<MovieFile>>()), Times.Never());
            Mocker.GetMock<ISidecarOwnership>().Verify(v => v.GetFolderFiles(It.IsAny<Movie>()), Times.Never());
            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(11);
            _upserted[0].RelativePath.Should().Be("s1.en.srt");
        }
    }
}
