using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Consumers.Xbmc;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Credits;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Metadata.Consumers.Xbmc
{
    [TestFixture]
    public class MovieMetadataFixture : CoreTest<XbmcMetadata>
    {
        private Movie _movie;
        private MovieFile _movieFile;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new MetadataDefinition
            {
                Settings = new XbmcMetadataSettings { MovieMetadata = true }
            };

            // Shaped like a movie loaded from the database: Studio is never persisted, only StudioTitle is
            _movie = new Movie
            {
                Path = @"C:\Test\Scenes\Bratty Sis\Stepsis Has A New Fetish (2026)".AsOsAgnostic(),
                MovieMetadata = new MovieMetadata
                {
                    Title = "Stepsis Has A New Fetish",
                    StudioTitle = "Bratty Sis",
                    Studio = null
                }
            };

            _movieFile = new MovieFile
            {
                RelativePath = "Bratty Sis - Stepsis Has A New Fetish (2026).mkv"
            };

            Mocker.GetMock<ICreditService>()
                  .Setup(s => s.GetAllCreditsForMovieMetadata(It.IsAny<int>()))
                  .Returns(new List<Credit>());
        }

        [Test]
        public void should_write_studio_title_when_studio_resource_is_not_loaded()
        {
            GetStudioElement().Value.Should().Be("Bratty Sis");
        }

        [Test]
        public void should_write_empty_studio_when_movie_has_no_studio()
        {
            _movie.MovieMetadata.Value.StudioTitle = null;

            GetStudioElement().Value.Should().BeEmpty();
        }

        [TestCase("en-US")]
        [TestCase("de-DE")]
        public void should_write_framerate_with_invariant_decimal_separator(string culture)
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);

                _movieFile.MediaInfo = new MediaInfoModel
                {
                    Width = 1920,
                    Height = 1080,
                    VideoFps = 29.97m
                };

                var result = Subject.MovieMetadata(_movie, _movieFile);

                XDocument.Parse(result.Contents).Root
                         .Element("fileinfo").Element("streamdetails").Element("video").Element("framerate")
                         .Value.Should().Be("29.97");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        [TestCase(Movies.MetadataSource.TpdbScene, "https://theporndb.net/scenes/6c1d4e2a-9b8f-4c3d-a2e1-7f0b5d9c8e41", "stashdb.org")]
        [TestCase(Movies.MetadataSource.Stash, "https://stashdb.org/scenes/6c1d4e2a-9b8f-4c3d-a2e1-7f0b5d9c8e41", "theporndb.net")]
        public void should_link_tpdb_scene_in_nfo(Movies.MetadataSource source, string expectedUrl, string otherHost)
        {
            Subject.Definition.Settings = new XbmcMetadataSettings { MovieMetadata = true, MovieMetadataURL = true };

            _movie.MovieMetadata.Value.ItemType = ItemType.Scene;
            _movie.MovieMetadata.Value.MetadataSource = source;
            _movie.MovieMetadata.Value.ForeignId = "6c1d4e2a-9b8f-4c3d-a2e1-7f0b5d9c8e41";
            _movie.MovieMetadata.Value.StashId = null;

            var result = Subject.MovieMetadata(_movie, _movieFile);

            result.Contents.Should().Contain(expectedUrl);
            result.Contents.Should().NotContain(otherHost);
        }

        [Test]
        public void should_write_per_file_nfo_in_shared_folder_even_with_use_movie_nfo()
        {
            GivenSharedFolder();
            Subject.Definition.Settings = new XbmcMetadataSettings { MovieMetadata = true, UseMovieNfo = true };

            var result = Subject.MovieMetadata(_movie, _movieFile);

            result.RelativePath.Should().Be("Bratty Sis - Stepsis Has A New Fetish (2026).nfo");
            XDocument.Parse(result.Contents).Root.Name.LocalName.Should().Be("movie");
        }

        [Test]
        public void should_write_movie_nfo_in_exclusive_folder_with_use_movie_nfo()
        {
            Subject.Definition.Settings = new XbmcMetadataSettings { MovieMetadata = true, UseMovieNfo = true };

            var result = Subject.MovieMetadata(_movie, _movieFile);

            result.RelativePath.Should().Be("movie.nfo");
        }

        [Test]
        public void should_write_per_file_images_in_shared_folder()
        {
            GivenSharedFolder();
            GivenImages();

            var images = Subject.MovieImages(_movie, _movieFile);

            images.Select(i => i.RelativePath).Should().BeEquivalentTo(
                "Bratty Sis - Stepsis Has A New Fetish (2026)-poster.jpg",
                "Bratty Sis - Stepsis Has A New Fetish (2026)-fanart.jpg");
        }

        [Test]
        public void should_write_no_images_in_shared_folder_without_a_file()
        {
            GivenSharedFolder();
            GivenImages();

            Subject.MovieImages(_movie, null).Should().BeEmpty();
        }

        [Test]
        public void should_write_folder_level_images_in_exclusive_folder()
        {
            GivenImages();

            var images = Subject.MovieImages(_movie, _movieFile);

            images.Select(i => i.RelativePath).Should().BeEquivalentTo("poster.jpg", "fanart.jpg");
        }

        [Test]
        public void should_rename_per_file_image_with_its_video()
        {
            GivenSharedFolder();

            var metadataFile = new MetadataFile
            {
                MovieFileId = 7,
                Type = MetadataType.MovieImage,
                RelativePath = "Old Name-poster.jpg"
            };

            var path = Subject.GetFilenameAfterMove(_movie, _movieFile, metadataFile);

            path.Should().Be(Path.Combine(_movie.Path, "Bratty Sis - Stepsis Has A New Fetish (2026)-poster.jpg"));
        }

        [Test]
        public void should_move_tracked_movie_nfo_to_per_file_nfo_in_shared_folder()
        {
            GivenSharedFolder();
            Subject.Definition.Settings = new XbmcMetadataSettings { MovieMetadata = true, UseMovieNfo = true };

            var metadataFile = new MetadataFile { Type = MetadataType.MovieMetadata, RelativePath = "movie.nfo" };

            var path = Subject.GetFilenameAfterMove(_movie, _movieFile, metadataFile);

            path.Should().Be(Path.Combine(_movie.Path, "Bratty Sis - Stepsis Has A New Fetish (2026).nfo"));
        }

        private void GivenSharedFolder()
        {
            Mocker.GetMock<ISceneFolderLayout>()
                  .Setup(s => s.IsShared(It.IsAny<Movie>()))
                  .Returns(true);
        }

        private void GivenImages()
        {
            Subject.Definition.Settings = new XbmcMetadataSettings { MovieImages = true };

            _movie.MovieMetadata.Value.Images = new List<MediaCover.MediaCover>
            {
                new() { CoverType = MediaCoverTypes.Poster },
                new() { CoverType = MediaCoverTypes.Fanart }
            };

            Mocker.GetMock<IMapCoversToLocal>()
                  .Setup(s => s.GetMovieCoverPath(It.IsAny<int>(), It.IsAny<MediaCoverTypes>()))
                  .Returns<int, MediaCoverTypes, int?>((_, type, _) => $"/covers/{type.ToString().ToLowerInvariant()}.jpg");
        }

        private XElement GetStudioElement()
        {
            var result = Subject.MovieMetadata(_movie, _movieFile);

            return XDocument.Parse(result.Contents).Root.Element("studio");
        }
    }
}
