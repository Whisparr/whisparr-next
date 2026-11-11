using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Others
{
    [TestFixture]
    public class ExistingOtherExtraImporterFixture : CoreTest<ExistingOtherExtraImporter>
    {
        private Movie _movie;
        private string _folder;
        private List<OtherExtraFile> _upserted;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\Test\Scenes\Studio".AsOsAgnostic();
            _movie = new Movie { Id = 1, Path = _folder, MovieFileId = 99 };
            _upserted = new List<OtherExtraFile>();

            Mocker.GetMock<IExtraFileService<OtherExtraFile>>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<OtherExtraFile>());
            Mocker.GetMock<IExtraFileService<OtherExtraFile>>()
                  .Setup(s => s.Upsert(It.IsAny<List<OtherExtraFile>>()))
                  .Callback<List<OtherExtraFile>>(l => _upserted.AddRange(l));

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.ParseMinimalPathMovieInfo(It.IsAny<string>()))
                  .Returns(new ParsedMovieInfo());
        }

        [Test]
        public void importer_uses_resolved_owner()
        {
            var mine = Path.Combine(_folder, "s1.jpg");
            var unresolved = Path.Combine(_folder, "folder.jpg");
            var owners = new Dictionary<string, int> { { mine, 11 } };

            Subject.ProcessFiles(_movie, new List<string> { mine, unresolved }, new List<string>(), null, owners).ToList();

            Mocker.GetMock<ISidecarOwnership>().Verify(v => v.FindOwner(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<List<MovieFile>>()), Times.Never());
            Mocker.GetMock<ISidecarOwnership>().Verify(v => v.GetFolderFiles(It.IsAny<Movie>()), Times.Never());
            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(11);
            _upserted[0].RelativePath.Should().Be("s1.jpg");
        }
    }
}
