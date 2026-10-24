using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Subtitles
{
    [TestFixture]
    public class SubtitleServiceFixture : CoreTest<SubtitleService>
    {
        private Movie _movie;
        private MovieFile _movieFile;
        private LocalMovie _localMovie;

        private string _MovieFolder;
        private string _releaseFolder;

        [SetUp]
        public void Setup()
        {
            _MovieFolder = @"C:\Test\Movies\Movie Title".AsOsAgnostic();
            _releaseFolder = @"C:\Test\Unsorted Movies\Movie.Title.2022".AsOsAgnostic();

            _movie = Builder<Movie>.CreateNew()
                                     .With(s => s.Path = _MovieFolder)
                                     .Build();

            _movieFile = Builder<MovieFile>.CreateNew()
                                               .With(f => f.Path = Path.Combine(_movie.Path, "Movie Title - 2022.mkv").AsOsAgnostic())
                                               .With(f => f.RelativePath = @"Movie Title - 2022.mkv".AsOsAgnostic())
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

            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetParentFolder(It.IsAny<string>()))
                  .Returns((string path) => Directory.GetParent(path).FullName);

            Mocker.GetMock<IDetectSample>().Setup(s => s.IsSample(It.IsAny<MovieMetadata>(), It.IsAny<string>()))
                  .Returns(DetectSampleResult.NotSample);
        }

        [Test]
        [TestCase("Movie.Title.2022.en.nfo")]
        public void should_not_import_non_subtitle_file(string filePath)
        {
            var files = new List<string> { Path.Combine(_releaseFolder, filePath).AsOsAgnostic() };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Count.Should().Be(0);
        }

        [Test]
        [TestCase("Movie Title - 2022.srt", "Movie Title - 2022.srt")]
        [TestCase("Movie.Title.2022.en.srt", "Movie Title - 2022.en.srt")]
        [TestCase("Movie.Title.2022.english.srt", "Movie Title - 2022.en.srt")]
        [TestCase("Movie Title 2022_en_sdh_forced.srt", "Movie Title - 2022.en.sdh.forced.srt")]
        [TestCase("Movie_Title_2022 en.srt", "Movie Title - 2022.en.srt")]
        [TestCase(@"Subs\Movie.Title.2022\2_en.srt", "Movie Title - 2022.en.srt")]
        [TestCase("sub.srt", "Movie Title - 2022.srt")]
        public void should_import_matching_subtitle_file(string filePath, string expectedOutputPath)
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
                Path.Combine(_releaseFolder, @"Subs\Movie.Title.2022\2_en.srt").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Movie.Title.2022.en.srt").AsOsAgnostic()
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Should().ContainSingle();
        }

        [Test]
        public void should_not_import_subtitle_of_a_sibling_whose_name_extends_ours()
        {
            // A neutral folder, so the title/year fallback can't match by folder name
            _releaseFolder = @"C:\Test\Unsorted Movies\Studio".AsOsAgnostic();
            _localMovie.Path = Path.Combine(_releaseFolder, "Title.mkv").AsOsAgnostic();

            var files = new List<string>
            {
                Path.Combine(_releaseFolder, "Title.en.srt").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Title 2.en.srt").AsOsAgnostic()
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Should().ContainSingle();
        }

        [Test]
        public void should_import_multiple_subtitle_files_per_language()
        {
            var files = new List<string>
            {
                Path.Combine(_releaseFolder, "Movie.Title.2022.en.srt").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Movie.Title.2022.eng.srt").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Subs", "Movie_Title_2022_en_forced.srt").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Subs", "Movie.Title.2022", "2_fr.srt").AsOsAgnostic()
            };

            var expectedOutputs = new string[]
            {
                "Movie Title - 2022.1.en.srt",
                "Movie Title - 2022.2.en.srt",
                "Movie Title - 2022.en.forced.srt",
                "Movie Title - 2022.fr.srt",
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Count.Should().Be(expectedOutputs.Length);

            for (var i = 0; i < expectedOutputs.Length; i++)
            {
                results[i].RelativePath.AsOsAgnostic().PathEquals(expectedOutputs[i].AsOsAgnostic()).Should().Be(true);
            }
        }

        [Test]
        public void should_import_multiple_subtitle_files_per_language_with_tags()
        {
            var files = new List<string>
            {
                Path.Combine(_releaseFolder, "Movie.Title.2022.en.forced.cc.srt").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Movie.Title.2022.other.en.forced.cc.srt").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Movie.Title.2022.en.forced.sdh.srt").AsOsAgnostic(),
                Path.Combine(_releaseFolder, "Movie.Title.2022.en.forced.default.srt").AsOsAgnostic(),
            };

            var expectedOutputs = new[]
            {
                "Movie Title - 2022.1.en.forced.cc.srt",
                "Movie Title - 2022.2.en.forced.cc.srt",
                "Movie Title - 2022.en.forced.sdh.srt",
                "Movie Title - 2022.en.forced.default.srt"
            };

            var results = Subject.ImportFiles(_localMovie, _movieFile, files, true).ToList();

            results.Count.Should().Be(expectedOutputs.Length);

            for (var i = 0; i < expectedOutputs.Length; i++)
            {
                results[i].RelativePath.AsOsAgnostic().PathEquals(expectedOutputs[i].AsOsAgnostic()).Should().Be(true);
            }
        }

        [Test]
        [TestCase(@"Subs\2_en.srt", "Movie Title - 2022.en.srt")]
        public void should_import_unmatching_subtitle_file_if_only_episode(string filePath, string expectedOutputPath)
        {
            var subtitleFile = Path.Combine(_releaseFolder, filePath).AsOsAgnostic();

            var sampleFile = Path.Combine(_movie.Path, "Movie Title - 2022.sample.mkv").AsOsAgnostic();

            var videoFiles = new string[]
            {
                _localMovie.Path,
                sampleFile
            };

            Mocker.GetMock<IDiskProvider>().Setup(s => s.GetFiles(It.IsAny<string>(), true))
                  .Returns(videoFiles);

            Mocker.GetMock<IDetectSample>().Setup(s => s.IsSample(It.IsAny<MovieMetadata>(), sampleFile))
                  .Returns(DetectSampleResult.Sample);

            var results = Subject.ImportFiles(_localMovie, _movieFile, new List<string> { subtitleFile }, true).ToList();

            results.Count.Should().Be(1);

            results[0].RelativePath.AsOsAgnostic().PathEquals(expectedOutputPath.AsOsAgnostic()).Should().Be(true);

            ExceptionVerification.ExpectedWarns(1);
        }

        private RenamedMovieFile GivenRenameIntoStudioFolder(string subtitleRelativePath, string previousRelativePath = "s1.mp4")
        {
            var studioFolder = @"C:\lib\scenes\Shady Spa".AsOsAgnostic();
            var oldSceneFolder = Path.Combine(studioFolder, "2020 - Buffy [id]");

            _movie.Path = studioFolder;
            _movieFile.RelativePath = "2020 - Buffy [id].mp4";

            var subtitle = new SubtitleFile
            {
                MovieId = _movie.Id,
                MovieFileId = _movieFile.Id,
                RelativePath = subtitleRelativePath.AsOsAgnostic(),
                Extension = ".srt",
                Language = Language.English
            };

            Mocker.GetMock<ISubtitleFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<SubtitleFile> { subtitle });

            return new RenamedMovieFile
            {
                MovieFile = _movieFile,
                PreviousRelativePath = previousRelativePath.AsOsAgnostic(),
                PreviousPath = Path.Combine(oldSceneFolder, previousRelativePath.AsOsAgnostic())
            };
        }

        [Test]
        public void subtitle_moves_with_video_when_rename_changes_folder()
        {
            var renamed = GivenRenameIntoStudioFolder("s1.en.srt");

            var results = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.MoveFile(
                      Path.Combine(@"C:\lib\scenes\Shady Spa\2020 - Buffy [id]".AsOsAgnostic(), "s1.en.srt"),
                      Path.Combine(@"C:\lib\scenes\Shady Spa".AsOsAgnostic(), "2020 - Buffy [id].en.srt"),
                      false),
                      Times.Once());

            results.Should().ContainSingle();
            results[0].RelativePath.Should().Be("2020 - Buffy [id].en.srt");
        }

        [TestCase(null)]
        [TestCase(0)]
        public void subtitle_without_a_file_link_moves_when_the_movie_has_one_file(int? linkedFileId)
        {
            var renamed = GivenRenameIntoStudioFolder("s1.en.srt");

            Mocker.GetMock<ISubtitleFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<SubtitleFile>
                  {
                      new() { MovieId = _movie.Id, MovieFileId = linkedFileId, RelativePath = "s1.en.srt", Extension = ".srt", Language = Language.English }
                  });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(_movie.Id)).Returns(new List<MovieFile> { _movieFile });

            var results = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            results.Should().ContainSingle();
            results[0].RelativePath.Should().Be("2020 - Buffy [id].en.srt");
            results[0].MovieFileId.Should().Be(_movieFile.Id);
        }

        [Test]
        public void unlinked_subtitle_in_a_shared_folder_moves_only_when_the_file_owns_it()
        {
            var renamed = GivenRenameIntoStudioFolder("s1.en.srt");
            var other = new SubtitleFile { MovieId = _movie.Id, MovieFileId = null, RelativePath = "other.en.srt", Extension = ".srt", Language = Language.English };

            Mocker.GetMock<ISubtitleFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<SubtitleFile>
                  {
                      new() { MovieId = _movie.Id, MovieFileId = null, RelativePath = "s1.en.srt", Extension = ".srt", Language = Language.English },
                      other
                  });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(_movie.Id)).Returns(new List<MovieFile> { _movieFile });
            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(_movie)).Returns(true);
            Mocker.GetMock<ISidecarOwnership>().Setup(s => s.GetFolderFiles(_movie)).Returns(new List<MovieFile> { _movieFile });
            Mocker.GetMock<ISidecarOwnership>()
                  .Setup(s => s.FindOwner(_movie, It.IsAny<string>(), It.IsAny<List<MovieFile>>()))
                  .Returns<Movie, string, List<MovieFile>>((m, p, f) => Path.GetFileName(p) == "s1.en.srt" ? _movieFile : null);

            var results = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            results.Should().ContainSingle();
            results[0].RelativePath.Should().Be("2020 - Buffy [id].en.srt");
        }

        [Test]
        public void subtitle_in_a_subfolder_is_resolved_against_the_old_scene_folder()
        {
            var renamed = GivenRenameIntoStudioFolder(@"Subs\s1.en.srt");

            Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.MoveFile(
                      Path.Combine(@"C:\lib\scenes\Shady Spa\2020 - Buffy [id]".AsOsAgnostic(), "Subs", "s1.en.srt"),
                      Path.Combine(@"C:\lib\scenes\Shady Spa".AsOsAgnostic(), "2020 - Buffy [id].en.srt"),
                      false),
                      Times.Once());
        }

        [Test]
        public void subtitle_is_not_moved_over_an_existing_file()
        {
            var renamed = GivenRenameIntoStudioFolder("s1.en.srt");

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(Path.Combine(@"C:\lib\scenes\Shady Spa".AsOsAgnostic(), "2020 - Buffy [id].en.srt")))
                  .Returns(true);

            var results = Subject.MoveFilesAfterRename(_movie, new List<RenamedMovieFile> { renamed }).ToList();

            results.Should().BeEmpty();

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
