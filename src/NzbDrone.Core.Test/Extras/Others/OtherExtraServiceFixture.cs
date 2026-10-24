using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Others
{
    [TestFixture]
    public class OtherExtraServiceFixture : CoreTest<OtherExtraService>
    {
        private Movie _movie;
        private MovieFile _movieFile;
        private LocalMovie _localMovie;

        private string _movieFolder;
        private string _releaseFolder;

        [SetUp]
        public void Setup()
        {
            _movieFolder = @"C:\Test\Movies\Movie Title".AsOsAgnostic();
            _releaseFolder = @"C:\Test\Unsorted Movies\Movie.Title.2022".AsOsAgnostic();

            _movie = Builder<Movie>.CreateNew()
                                     .With(s => s.Path = _movieFolder)
                                     .Build();

            _movieFile = Builder<MovieFile>.CreateNew()
                                               .With(f => f.Path = Path.Combine(_movie.Path, "Movie Title - 2022.mkv").AsOsAgnostic())
                                               .With(f => f.RelativePath = @"Movie Title - 2022.mkv")
                                               .Build();

            _localMovie = Builder<LocalMovie>.CreateNew()
                                                 .With(l => l.Movie = _movie)
                                                 .With(l => l.Path = Path.Combine(_releaseFolder, "Movie.Title.2022.mkv").AsOsAgnostic())
                                                 .With(l => l.FileMovieInfo = new ParsedMovieInfo
                                                 {
                                                     MovieTitles = new List<string> { "Movie Title" },
                                                     Year = 2022
                                                 })
                                                 .Build();
        }

        [Test]
        [TestCase("Movie Title - 2022.nfo", "Movie Title - 2022.nfo")]
        [TestCase("Movie.Title.2022.nfo", "Movie Title - 2022.nfo")]
        [TestCase("Movie Title 2022.nfo", "Movie Title - 2022.nfo")]
        [TestCase("Movie_Title_2022.nfo", "Movie Title - 2022.nfo")]
        [TestCase(@"Movie.Title.2022\thumb.jpg", "Movie Title - 2022.jpg")]
        public void should_import_matching_file(string filePath, string expectedOutputPath)
        {
            var files = new List<string> { Path.Combine(_releaseFolder, filePath).AsOsAgnostic() };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Count.Should().Be(1);

            results[0].RelativePath.AsOsAgnostic().PathEquals(expectedOutputPath.AsOsAgnostic()).Should().Be(true);
        }

        [Test]
        public void shared_folder_import_skips_title_year_fallback()
        {
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(_movie)).Returns(true);

            var files = new List<string>
            {
                Path.Combine(_releaseFolder, @"Movie.Title.2022\thumb.jpg").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Movie.Title.2022.nfo").AsOsAgnostic()
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Should().ContainSingle();
            results[0].RelativePath.Should().EndWith(".nfo");
        }

        [Test]
        public void should_not_import_extra_of_a_sibling_whose_name_extends_ours()
        {
            // A neutral folder, so the title/year fallback can't match by folder name
            _releaseFolder = @"C:\Test\Unsorted Movies\Studio".AsOsAgnostic();
            _localMovie.Path = Path.Combine(_releaseFolder, "Title.mkv").AsOsAgnostic();

            var files = new List<string>
            {
                Path.Combine(_releaseFolder, "Title.mka").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Title 2.mka").AsOsAgnostic()
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Should().ContainSingle();
        }

        [Test]
        public void should_not_import_multiple_nfo_files()
        {
            var files = new List<string>
            {
                Path.Combine(_releaseFolder, "Movie.Title.2022.nfo").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Movie_Title_2022.nfo").AsOsAgnostic(),
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Count.Should().Be(1);
        }

        [Test]
        [TestCase(@"audio_folder_1\Movie.Title.2022.mka", @"audio_folder_2\Movie.Title.2022.mka", "Movie Title - 2022.1.mka", "Movie Title - 2022.2.mka")]
        public void should_import_all_files_with_same_name(string firstExtraFilePath, string secondExtraFilePath, string firstOutputPath, string secondOutputPath)
        {
            var files = new List<string>
            {
                Path.Combine(_releaseFolder, firstExtraFilePath).AsOsAgnostic(),
                Path.Combine(_releaseFolder, secondExtraFilePath).AsOsAgnostic()
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Count.Should().Be(2);

            results[0].RelativePath.AsOsAgnostic().PathEquals(firstOutputPath.AsOsAgnostic()).Should().Be(true);
            results[1].RelativePath.AsOsAgnostic().PathEquals(secondOutputPath.AsOsAgnostic()).Should().Be(true);
        }

        [Test]
        public void should_increment_suffix_for_each_duplicate_file()
        {
            var files = new List<string>
            {
                Path.Combine(_releaseFolder, @"audio_folder_1\Movie.Title.2022.mka").AsOsAgnostic(),
                Path.Combine(_releaseFolder, @"audio_folder_2\Movie.Title.2022.mka").AsOsAgnostic(),
                Path.Combine(_releaseFolder, @"audio_folder_3\Movie.Title.2022.mka").AsOsAgnostic(),
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Count.Should().Be(3);
            results[0].RelativePath.AsOsAgnostic().PathEquals("Movie Title - 2022.1.mka".AsOsAgnostic()).Should().Be(true);
            results[1].RelativePath.AsOsAgnostic().PathEquals("Movie Title - 2022.2.mka".AsOsAgnostic()).Should().Be(true);
            results[2].RelativePath.AsOsAgnostic().PathEquals("Movie Title - 2022.3.mka".AsOsAgnostic()).Should().Be(true);
        }

        [Test]
        public void should_suffix_files_matched_by_filename_prefix()
        {
            var files = new List<string>
            {
                Path.Combine(_releaseFolder, "Movie.Title.2022.behind_scenes.mka").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Movie.Title.2022.commentary.mka").AsOsAgnostic(),
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Count.Should().Be(2);
            results[0].RelativePath.AsOsAgnostic().PathEquals("Movie Title - 2022.1.mka".AsOsAgnostic()).Should().Be(true);
            results[1].RelativePath.AsOsAgnostic().PathEquals("Movie Title - 2022.2.mka".AsOsAgnostic()).Should().Be(true);
        }

        [Test]
        public void should_suffix_files_matched_by_both_filename_and_movie_info()
        {
            var files = new List<string>
            {
                Path.Combine(_releaseFolder, "Movie.Title.2022.behind_scenes.mka").AsOsAgnostic(),
                Path.Combine(_releaseFolder, @"extras\Movie Title 2022.mka").AsOsAgnostic(),
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Count.Should().Be(2);
            results[0].RelativePath.AsOsAgnostic().PathEquals("Movie Title - 2022.1.mka".AsOsAgnostic()).Should().Be(true);
            results[1].RelativePath.AsOsAgnostic().PathEquals("Movie Title - 2022.2.mka".AsOsAgnostic()).Should().Be(true);
        }

        [Test]
        public void should_not_suffix_when_other_files_do_not_match_movie()
        {
            var files = new List<string>
            {
                Path.Combine(_releaseFolder, "Movie.Title.2022.mka").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Other.Movie.2019.mka").AsOsAgnostic(),
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Count.Should().Be(1);
            results[0].RelativePath.AsOsAgnostic().PathEquals("Movie Title - 2022.mka".AsOsAgnostic()).Should().Be(true);
        }

        [Test]
        public void other_extra_moves_with_video_when_rename_changes_folder()
        {
            var studioFolder = @"C:\lib\scenes\Shady Spa".AsOsAgnostic();
            var oldSceneFolder = Path.Combine(studioFolder, "2020 - Buffy [id]");

            _movie.Path = studioFolder;
            _movieFile.RelativePath = "2020 - Buffy [id].mp4";

            var extra = new OtherExtraFile
            {
                MovieId = _movie.Id,
                MovieFileId = _movieFile.Id,
                RelativePath = "s1.nfo",
                Extension = ".nfo"
            };

            Mocker.GetMock<IOtherExtraFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<OtherExtraFile> { extra });

            var renamed = new RenamedMovieFile
            {
                MovieFile = _movieFile,
                PreviousRelativePath = "s1.mp4",
                PreviousPath = Path.Combine(oldSceneFolder, "s1.mp4")
            };

            var results = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.MoveFile(
                      Path.Combine(oldSceneFolder, "s1.nfo"),
                      Path.Combine(studioFolder, "2020 - Buffy [id].nfo"),
                      false),
                      Times.Once());

            results.Should().ContainSingle();
            results[0].RelativePath.Should().Be("2020 - Buffy [id].nfo");
        }

        [Test]
        public void skipped_extra_move_never_leaves_a_row_resolving_into_the_new_folder()
        {
            var studioFolder = @"C:\lib\scenes\Shady Spa".AsOsAgnostic();
            var oldSceneFolder = Path.Combine(@"C:\lib\scenes\Old Studio".AsOsAgnostic(), "2020 - Buffy [id]");

            _movie.Path = studioFolder;
            _movieFile.RelativePath = "2020 - Buffy [id].mp4";

            var extra = new OtherExtraFile
            {
                Id = 31,
                MovieId = _movie.Id,
                MovieFileId = _movieFile.Id,
                RelativePath = "s1.nfo",
                Extension = ".nfo"
            };

            Mocker.GetMock<IOtherExtraFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<OtherExtraFile> { extra });

            // Something already sits at the destination, so the move is skipped
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(Path.Combine(studioFolder, "2020 - Buffy [id].nfo")))
                  .Returns(true);

            var renamed = new RenamedMovieFile
            {
                MovieFile = _movieFile,
                PreviousRelativePath = "s1.mp4",
                PreviousPath = Path.Combine(oldSceneFolder, "s1.mp4")
            };

            var results = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            results.Should().BeEmpty();
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());

            // "s1.nfo" would now resolve to the studio folder's own s1.nfo, so the row goes (the file stays on disk)
            Mocker.GetMock<IOtherExtraFileService>()
                  .Verify(v => v.DeleteMany(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 31 }))), Times.Once());
        }

        [Test]
        public void skipped_extra_move_repoints_row_when_the_file_sits_under_the_new_folder()
        {
            var studioFolder = @"C:\lib\scenes\Shady Spa".AsOsAgnostic();
            var oldSceneFolder = Path.Combine(studioFolder, "2020 - Buffy [id]");

            _movie.Path = studioFolder;
            _movieFile.RelativePath = "2020 - Buffy [id].mp4";

            var extra = new OtherExtraFile
            {
                Id = 31,
                MovieId = _movie.Id,
                MovieFileId = _movieFile.Id,
                RelativePath = "s1.nfo",
                Extension = ".nfo"
            };

            Mocker.GetMock<IOtherExtraFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<OtherExtraFile> { extra });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(Path.Combine(studioFolder, "2020 - Buffy [id].nfo")))
                  .Returns(true);

            var renamed = new RenamedMovieFile
            {
                MovieFile = _movieFile,
                PreviousRelativePath = "s1.mp4",
                PreviousPath = Path.Combine(oldSceneFolder, "s1.mp4")
            };

            var results = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            results.Should().ContainSingle().Which.RelativePath.Should().Be(Path.Combine("2020 - Buffy [id]", "s1.nfo"));
            Mocker.GetMock<IOtherExtraFileService>()
                  .Verify(v => v.DeleteMany(It.Is<IEnumerable<int>>(ids => ids.Any())), Times.Never());
        }

        [TestCase(null)]
        [TestCase(0)]
        public void other_extra_without_a_file_link_moves_when_the_movie_has_one_file(int? linkedFileId)
        {
            var studioFolder = @"C:\lib\scenes\Shady Spa".AsOsAgnostic();
            var oldSceneFolder = Path.Combine(studioFolder, "2020 - Buffy [id]");

            _movie.Path = studioFolder;
            _movieFile.RelativePath = "2020 - Buffy [id].mp4";

            Mocker.GetMock<IOtherExtraFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<OtherExtraFile>
                  {
                      new() { MovieId = _movie.Id, MovieFileId = linkedFileId, RelativePath = "s1.nfo", Extension = ".nfo" }
                  });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(_movie.Id)).Returns(new List<MovieFile> { _movieFile });

            var renamed = new RenamedMovieFile
            {
                MovieFile = _movieFile,
                PreviousRelativePath = "s1.mp4",
                PreviousPath = Path.Combine(oldSceneFolder, "s1.mp4")
            };

            var results = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            results.Should().ContainSingle();
            results[0].RelativePath.Should().Be("2020 - Buffy [id].nfo");
            results[0].MovieFileId.Should().Be(_movieFile.Id);
        }
    }
}
