using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras
{
    [TestFixture]
    public class SharedFolderSideFilesFixture : CoreTest<SharedFolderSideFiles>
    {
        private string _folder;
        private Movie _s1;
        private Movie _s2;
        private List<int> _itemIds;

        [SetUp]
        public void Setup()
        {
            _folder = @"C:\Test\Scenes\Studio".AsOsAgnostic();
            _s1 = new Movie { Id = 1, Path = _folder };
            _s2 = new Movie { Id = 2, Path = _folder };
            _itemIds = new List<int> { _s1.Id, _s2.Id };

            Mocker.GetMock<IExtraFileService<MetadataFile>>()
                  .Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<MetadataFile>());
            Mocker.GetMock<IExtraFileService<OtherExtraFile>>()
                  .Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<OtherExtraFile>());
            Mocker.GetMock<IExtraFileService<SubtitleFile>>()
                  .Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<SubtitleFile>());
        }

        private string InFolder(string name) => Path.Combine(_folder, name);

        private void GivenFindScene(Movie scene)
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindScene(It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), It.IsAny<SearchCriteriaBase>()))
                  .Returns(scene);
        }

        private void VerifyFindScene(Times times)
        {
            Mocker.GetMock<IMovieService>()
                  .Verify(v => v.FindScene(It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), It.IsAny<SearchCriteriaBase>()), times);
        }

        [Test]
        public void tracked_side_file_is_not_parsed()
        {
            var file = InFolder("Movie.Title.2022.nfo");

            Mocker.GetMock<IExtraFileService<MetadataFile>>()
                  .Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<MetadataFile> { new() { Id = 5, MovieId = _s2.Id, RelativePath = "Movie.Title.2022.nfo" } });
            GivenFindScene(_s1);

            var result = Subject.Assign(_folder, _itemIds, new List<string> { file }, new List<MovieFile>());

            VerifyFindScene(Times.Never());
            result.FilesByMovie[_s2.Id].Should().BeEquivalentTo(new[] { file });
            result.FilesByMovie.Where(f => f.Key != _s2.Id).SelectMany(f => f.Value).Should().BeEmpty();
            result.OwnerFileIds.Should().NotContainKey(file);
        }

        [Test]
        public void name_match_wins_over_parse()
        {
            var file = InFolder("Movie.Title.2022.nfo");
            var video = new MovieFile { Id = 11, MovieId = _s1.Id, RelativePath = "Movie.Title.2022.mp4" };
            var sibling = new MovieFile { Id = 22, MovieId = _s2.Id, RelativePath = "Other.mp4" };
            GivenFindScene(_s2);

            var result = Subject.Assign(_folder, _itemIds, new List<string> { file }, new List<MovieFile> { video, sibling });

            result.OwnerFileIds[file].Should().Be(video.Id);
            result.FilesByMovie[_s1.Id].Should().BeEquivalentTo(new[] { file });
            VerifyFindScene(Times.Never());
        }

        [Test]
        public void longest_name_wins()
        {
            var file = InFolder("scene-part2.srt");
            var short1 = new MovieFile { Id = 11, MovieId = _s1.Id, RelativePath = "scene.mp4" };
            var long2 = new MovieFile { Id = 22, MovieId = _s2.Id, RelativePath = "scene-part2.mp4" };

            var result = Subject.Assign(_folder, _itemIds, new List<string> { file }, new List<MovieFile> { short1, long2 });

            result.OwnerFileIds[file].Should().Be(long2.Id);
            result.FilesByMovie[_s2.Id].Should().BeEquivalentTo(new[] { file });
            result.FilesByMovie[_s1.Id].Should().BeEmpty();
        }

        [Test]
        public void name_match_ignores_case()
        {
            var file = InFolder("movie.title.EN.srt");
            var video = new MovieFile { Id = 11, MovieId = _s1.Id, RelativePath = "Movie.Title.mp4" };

            var result = Subject.Assign(_folder, _itemIds, new List<string> { file }, new List<MovieFile> { video });

            result.OwnerFileIds[file].Should().Be(video.Id);
            VerifyFindScene(Times.Never());
        }

        [Test]
        public void tie_stays_unowned()
        {
            var file = InFolder("Title.nfo");
            var a = new MovieFile { Id = 11, MovieId = _s1.Id, RelativePath = "Title.mp4" };
            var b = new MovieFile { Id = 22, MovieId = _s2.Id, RelativePath = "Title.mkv" };

            var result = Subject.Assign(_folder, _itemIds, new List<string> { file }, new List<MovieFile> { a, b });

            result.OwnerFileIds.Should().NotContainKey(file);
            result.FilesByMovie.SelectMany(f => f.Value).Should().BeEmpty();
            VerifyFindScene(Times.Never());
        }

        [Test]
        public void parsed_scene_in_the_folder_owns_the_file()
        {
            var file = InFolder("Movie.Title.2022.nfo");
            var video = new MovieFile { Id = 22, MovieId = _s2.Id, RelativePath = "Other.mp4" };
            GivenFindScene(_s2);

            var result = Subject.Assign(_folder, _itemIds, new List<string> { file }, new List<MovieFile> { video });

            result.OwnerFileIds[file].Should().Be(video.Id);
            result.FilesByMovie[_s2.Id].Should().BeEquivalentTo(new[] { file });
        }

        [Test]
        public void parsed_scene_outside_the_folder_is_unowned()
        {
            var file = InFolder("Movie.Title.2022.nfo");
            GivenFindScene(new Movie { Id = 99, Path = InFolder("Elsewhere") });

            var result = Subject.Assign(_folder, _itemIds, new List<string> { file }, new List<MovieFile>());

            result.OwnerFileIds.Should().BeEmpty();
        }

        [Test]
        public void generic_file_is_parsed_once_per_window()
        {
            var generic = InFolder("poster.jpg");
            var unknown = InFolder("Movie.Title.2022.jpg");
            var files = new List<string> { generic, unknown };

            var first = Subject.Assign(_folder, _itemIds, files, new List<MovieFile>());
            var second = Subject.Assign(_folder, _itemIds, files, new List<MovieFile>());

            VerifyFindScene(Times.Once());
            first.OwnerFileIds.Should().BeEmpty();
            second.OwnerFileIds.Should().BeEmpty();
            second.FilesByMovie.SelectMany(f => f.Value).Should().BeEmpty();
        }
    }
}
