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
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras
{
    [TestFixture]
    public class SidecarNameMatchesFixture
    {
        [TestCase("Title.en.srt", "Title")]
        [TestCase("Title-poster.jpg", "Title")]
        [TestCase("Title.nfo", "Title")]
        [TestCase("title.NFO", "Title")]
        [TestCase("Title", "Title")]
        public void should_match_at_a_boundary(string sidecar, string video)
        {
            SidecarName.SidecarNameMatches(sidecar, video).Should().BeTrue();
        }

        [TestCase("Title 2.nfo", "Title")]
        [TestCase("Titled.nfo", "Title")]
        [TestCase("Other.nfo", "Title")]
        [TestCase("", "Title")]
        public void should_not_match_without_a_boundary(string sidecar, string video)
        {
            SidecarName.SidecarNameMatches(sidecar, video).Should().BeFalse();
        }

        [Test]
        public void should_ignore_the_sidecar_directory()
        {
            SidecarName.SidecarNameMatches(Path.Combine("some", "dir", "Title.nfo"), "Title").Should().BeTrue();
        }
    }

    [TestFixture]
    public class SidecarOwnershipFixture : CoreTest<SidecarOwnership>
    {
        private string _folder;
        private Movie _scene1;
        private Movie _scene2;
        private MovieFile _file1;
        private MovieFile _file2;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\Test\Scenes\Studio".AsOsAgnostic();
            _scene1 = new Movie { Id = 1, Path = _folder };
            _scene2 = new Movie { Id = 2, Path = _folder };
            _file1 = new MovieFile { Id = 11, MovieId = 1, RelativePath = "Title.mp4" };
            _file2 = new MovieFile { Id = 22, MovieId = 2, RelativePath = "Title 2.mp4" };
        }

        private string Sidecar(string name)
        {
            return Path.Combine(_folder, name);
        }

        [Test]
        public void longest_base_name_wins()
        {
            var files = new List<MovieFile> { _file1, _file2 };

            Subject.FindOwner(_scene2, Sidecar("Title 2.en.srt"), files).Should().Be(_file2);
            Subject.FindOwner(_scene1, Sidecar("Title 2.en.srt"), files).Should().BeNull();
            Subject.FindOwner(_scene1, Sidecar("Title.en.srt"), files).Should().Be(_file1);
        }

        [Test]
        public void tie_is_unowned()
        {
            var other = new MovieFile { Id = 33, MovieId = 3, RelativePath = "Title.mkv" };
            var files = new List<MovieFile> { _file1, other };

            Subject.FindOwner(_scene1, Sidecar("Title.nfo"), files).Should().BeNull();
        }

        [Test]
        public void two_files_of_the_same_scene_are_not_a_tie()
        {
            var sameScene = new MovieFile { Id = 12, MovieId = 1, RelativePath = "Title.mkv" };
            var files = new List<MovieFile> { _file1, sameScene };

            Subject.FindOwner(_scene1, Sidecar("Title.nfo"), files).Should().NotBeNull();
        }

        [Test]
        public void parsed_scene_overrides_name_match()
        {
            var files = new List<MovieFile> { _file1, _file2 };
            var path = Sidecar("Movie.Title.2022.en.srt");

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindScene(It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(_scene2);

            Subject.FindOwner(_scene1, path, files).Should().BeNull();
            Subject.FindOwner(_scene2, path, files).Should().Be(_file2);
        }

        [Test]
        public void generic_names_are_unowned()
        {
            var files = new List<MovieFile> { _file1, _file2 };

            Subject.FindOwner(_scene1, Sidecar("movie.nfo"), files).Should().BeNull();
            Subject.FindOwner(_scene1, Sidecar("poster.jpg"), files).Should().BeNull();
        }

        [Test]
        public void generic_sidecar_in_studio_folder_does_not_resolve_scene_via_folder_name()
        {
            var folder = @"C:\Test\Scenes\Movie.Title.2022".AsOsAgnostic();
            var files = new List<MovieFile> { _file1, _file2 };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindScene(It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(_scene1);

            Subject.FindOwner(_scene1, Path.Combine(folder, "movie.nfo"), files).Should().BeNull();
            Subject.FindOwner(_scene1, Path.Combine(folder, "poster.jpg"), files).Should().BeNull();
        }

        [Test]
        public void should_gather_files_of_every_item_in_the_folder()
        {
            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.GetItemsInFolder(_folder))
                  .Returns(new List<MoviePathEntry> { new() { Id = 1 }, new() { Id = 2 } });

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(1)).Returns(new List<MovieFile> { _file1 });
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(2)).Returns(new List<MovieFile> { _file2 });

            Subject.GetFolderFiles(_scene1).Should().BeEquivalentTo(new[] { _file1, _file2 });
        }
    }

    [TestFixture]
    public class ExistingSubtitleImporterOwnershipFixture : CoreTest<ExistingSubtitleImporter>
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
        public void shared_folder_links_to_the_owning_file_and_skips_siblings()
        {
            var mine = Path.Combine(_folder, "Title.en.srt");
            var theirs = Path.Combine(_folder, "Title 2.en.srt");
            var owner = new MovieFile { Id = 11, MovieId = 1 };

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(_movie)).Returns(true);
            Mocker.GetMock<ISidecarOwnership>().Setup(s => s.GetFolderFiles(_movie)).Returns(new List<MovieFile> { owner });
            Mocker.GetMock<ISidecarOwnership>()
                  .Setup(s => s.FindOwner(_movie, mine, It.IsAny<List<MovieFile>>()))
                  .Returns(owner);

            Subject.ProcessFiles(_movie, new List<string> { mine, theirs }, new List<string>(), null).ToList();

            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(11);
            _upserted[0].RelativePath.Should().Be("Title.en.srt");
        }

        [Test]
        public void exclusive_folder_keeps_current_behaviour()
        {
            var path = Path.Combine(_folder, "Title 2.en.srt");

            Subject.ProcessFiles(_movie, new List<string> { path }, new List<string>(), null).ToList();

            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(99);
            Mocker.GetMock<ISidecarOwnership>().Verify(s => s.FindOwner(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<List<MovieFile>>()), Times.Never);
        }

        [Test]
        public void exclusive_folder_links_to_the_current_file_when_the_scanned_movie_is_stale()
        {
            // The scan publishes the movie it loaded before it imported the video, so MovieFileId is still 0
            _movie.MovieFileId = 0;
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(1)).Returns(new List<MovieFile> { new() { Id = 7, MovieId = 1 } });

            Subject.ProcessFiles(_movie, new List<string> { Path.Combine(_folder, "s1.en.srt") }, new List<string>(), null).ToList();

            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(7);
        }
    }

    [TestFixture]
    public class ExistingOtherExtraImporterOwnershipFixture : CoreTest<ExistingOtherExtraImporter>
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
        public void shared_folder_links_to_the_owning_file_and_skips_siblings()
        {
            var mine = Path.Combine(_folder, "Title-poster.jpg");
            var theirs = Path.Combine(_folder, "Title 2-poster.jpg");
            var owner = new MovieFile { Id = 11, MovieId = 1 };

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(_movie)).Returns(true);
            Mocker.GetMock<ISidecarOwnership>().Setup(s => s.GetFolderFiles(_movie)).Returns(new List<MovieFile> { owner });
            Mocker.GetMock<ISidecarOwnership>()
                  .Setup(s => s.FindOwner(_movie, mine, It.IsAny<List<MovieFile>>()))
                  .Returns(owner);

            Subject.ProcessFiles(_movie, new List<string> { mine, theirs }, new List<string>(), null).ToList();

            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(11);
        }

        [Test]
        public void exclusive_folder_keeps_current_behaviour()
        {
            var path = Path.Combine(_folder, "Title 2-poster.jpg");

            Subject.ProcessFiles(_movie, new List<string> { path }, new List<string>(), null).ToList();

            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(99);
        }

        [Test]
        public void exclusive_folder_links_to_the_current_file_when_the_scanned_movie_is_stale()
        {
            _movie.MovieFileId = 0;
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(1)).Returns(new List<MovieFile> { new() { Id = 7, MovieId = 1 } });

            Subject.ProcessFiles(_movie, new List<string> { Path.Combine(_folder, "s1-poster.jpg") }, new List<string>(), null).ToList();

            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(7);
        }
    }

    [TestFixture]
    public class ExistingMetadataImporterOwnershipFixture : CoreTest<ExistingMetadataImporter>
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
                        Type = MetadataType.MovieImage
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
        }

        [Test]
        public void shared_folder_links_to_the_owning_file_and_skips_generic_names()
        {
            var mine = Path.Combine(_folder, "Title-poster.jpg");
            var generic = Path.Combine(_folder, "poster.jpg");
            var owner = new MovieFile { Id = 11, MovieId = 1 };

            Mocker.GetMock<ISceneFolderLayout>().Setup(s => s.IsShared(_movie)).Returns(true);
            Mocker.GetMock<ISidecarOwnership>().Setup(s => s.GetFolderFiles(_movie)).Returns(new List<MovieFile> { owner });
            Mocker.GetMock<ISidecarOwnership>()
                  .Setup(s => s.FindOwner(_movie, mine, It.IsAny<List<MovieFile>>()))
                  .Returns(owner);

            Subject.ProcessFiles(_movie, new List<string> { mine, generic }, new List<string>(), null).ToList();

            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(11);
            _upserted[0].RelativePath.Should().Be("Title-poster.jpg");
        }

        [Test]
        public void exclusive_folder_keeps_current_behaviour()
        {
            var path = Path.Combine(_folder, "poster.jpg");

            Subject.ProcessFiles(_movie, new List<string> { path }, new List<string>(), null).ToList();

            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(99);
        }

        [Test]
        public void exclusive_folder_links_to_the_current_file_when_the_scanned_movie_is_stale()
        {
            _movie.MovieFileId = 0;
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetFilesByMovie(1)).Returns(new List<MovieFile> { new() { Id = 7, MovieId = 1 } });

            Subject.ProcessFiles(_movie, new List<string> { Path.Combine(_folder, "poster.jpg") }, new List<string>(), null).ToList();

            _upserted.Should().ContainSingle();
            _upserted[0].MovieFileId.Should().Be(7);
        }
    }
}
