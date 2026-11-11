using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras
{
    [TestFixture]
    public class ImportExistingExtraFilesBaseFixture : CoreTest<ExistingOtherExtraImporter>
    {
        private Movie _movie;
        private OtherExtraFile _row;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Path = @"C:\Test\Movies\Movie Title".AsOsAgnostic())
                                   .Build();

            _row = new OtherExtraFile { Id = 7, MovieId = _movie.Id, RelativePath = "a.srt.txt" };

            Mocker.GetMock<IExtraFileService<OtherExtraFile>>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<OtherExtraFile> { _row });
        }

        private void GivenFileExists(bool exists)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(d => d.FileExists(Path.Combine(_movie.Path, _row.RelativePath)))
                  .Returns(exists);
        }

        private void Clean()
        {
            Subject.FilterAndClean(_movie, new List<string>(), new List<string>(), false);
        }

        [Test]
        public void should_keep_side_file_row_missing_from_listing_but_on_disk()
        {
            GivenFileExists(true);

            Clean();

            Mocker.GetMock<IExtraFileService<OtherExtraFile>>()
                  .Verify(s => s.DeleteMany(It.Is<IEnumerable<int>>(ids => ids.Contains(_row.Id))), Times.Never());
        }

        [Test]
        public void should_delete_side_file_row_gone_from_disk()
        {
            GivenFileExists(false);

            Clean();

            Mocker.GetMock<IExtraFileService<OtherExtraFile>>()
                  .Verify(s => s.DeleteMany(It.Is<IEnumerable<int>>(ids => ids.Contains(_row.Id))), Times.Once());
        }
    }
}
