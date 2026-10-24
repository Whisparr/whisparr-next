using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.OrganizerTests
{
    [TestFixture]
    public class SceneFolderLayoutFixture : CoreTest<SceneFolderLayout>
    {
        private const string PerSceneFormat = "scenes/{Studio CleanTitle}/{Release Date} - {Scene CleanTitle} {[Scene ForeignId]}";
        private const string FlatFormat = "scenes/{Studio CleanNetwork}/{Studio CleanTitle}";

        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _namingConfig = new NamingConfig { SceneFolderFormat = PerSceneFormat };

            Mocker.GetMock<INamingConfigService>()
                  .Setup(s => s.GetConfig())
                  .Returns(_namingConfig);
        }

        [TestCase("scenes/{Studio CleanNetwork}/{Studio CleanTitle}")]
        [TestCase("{Studio Title}")]
        [TestCase("{Studio Network}/{Studio TitleSlug}")]
        [TestCase("scenes/Literal")]
        [TestCase("{Studio Title}/{Quality Title}")]
        [TestCase("{Studio Title}/{Quality Title} {Custom Formats}")]
        public void format_is_flat(string format)
        {
            SceneFolderLayout.FormatIsFlat(format).Should().BeTrue();
        }

        [TestCase("scenes/{Studio CleanTitle}/{Release Date} - {Scene CleanTitle} {[Scene ForeignId]}")]
        [TestCase("{Studio Title}/{Scene Title}")]
        [TestCase("{Studio Title}/{StashId}")]
        [TestCase("{Studio Title}/{Release Date}")]
        [TestCase("{Studio Title}/{Release Year}")]
        [TestCase("{Studio Title}/{Movie Title} ({Movie Year})")]
        [TestCase("{Studio Title}/{Original Title}")]
        [TestCase("{Studio Title}/{Studio Title} {(TmdbId)}")]
        [TestCase("{Studio Title}/{[ImdbId]}")]
        [TestCase("{Studio Title}/{Studio Title}-{[Scene ForeignId]}")]
        public void format_is_not_flat(string format)
        {
            SceneFolderLayout.FormatIsFlat(format).Should().BeFalse();
        }

        [TestCase(FlatFormat)]
        [TestCase(PerSceneFormat)]
        public void instance_is_flat_format_matches_static_rule(string format)
        {
            Subject.IsFlatFormat(format).Should().Be(SceneFolderLayout.FormatIsFlat(format));
        }

        [Test]
        public void is_flat_should_read_current_naming_config_after_it_is_saved()
        {
            Subject.IsFlat().Should().BeFalse();

            _namingConfig.SceneFolderFormat = FlatFormat;
            Subject.Handle(new NamingConfigSavedEvent());

            Subject.IsFlat().Should().BeTrue();
        }

        [Test]
        public void is_flat_should_not_read_the_naming_config_on_a_second_call()
        {
            GivenEntries(Entry(1, "/media/scenes/Studio/A", ItemType.Scene));

            Subject.IsFlat();
            Subject.IsFlat();
            Subject.IsShared(GivenItem(1, "/media/scenes/Studio/A", ItemType.Scene));

            Mocker.GetMock<INamingConfigService>().Verify(s => s.GetConfig(), Times.Once());
        }

        [Test]
        public void path_cache_is_cleared_on_movie_file_added_event()
        {
            GivenEntries(Entry(1, "/media/scenes/Studio/A", ItemType.Scene));

            Subject.GetItemsInFolder("/media/scenes/Studio/A");
            Subject.GetItemsInFolder("/media/scenes/Studio/A");

            Mocker.GetMock<IMovieRepository>().Verify(s => s.AllMoviePathEntries(), Times.Once());

            Subject.Handle(new MovieFileAddedEvent(new MovieFile()));
            Subject.GetItemsInFolder("/media/scenes/Studio/A");

            Mocker.GetMock<IMovieRepository>().Verify(s => s.AllMoviePathEntries(), Times.Exactly(2));
        }

        [Test]
        public void get_items_touching_folder_returns_items_in_under_and_above_the_folder_from_the_cache()
        {
            GivenEntries(
                Entry(1, "/media/scenes/Studio", ItemType.Scene),
                Entry(2, "/media/scenes/Studio/Movie", ItemType.Movie),
                Entry(3, "/media/scenes", ItemType.Scene),
                Entry(4, "/media/other", ItemType.Scene),
                Entry(5, null, ItemType.Scene));

            Subject.GetItemsTouchingFolder("/media/scenes/Studio").Select(e => e.Id).Should().BeEquivalentTo(new[] { 1, 2, 3 });
            Subject.GetItemsTouchingFolder("/media/scenes/Studio");

            Mocker.GetMock<IMovieRepository>().Verify(s => s.AllMoviePathEntries(), Times.Once());
        }

        [Test]
        public void is_shared_false_for_movies()
        {
            _namingConfig.SceneFolderFormat = FlatFormat;

            Subject.IsShared(GivenItem(1, "/media/movies/A", ItemType.Movie)).Should().BeFalse();
        }

        [Test]
        public void is_shared_true_for_scene_when_format_is_flat()
        {
            _namingConfig.SceneFolderFormat = FlatFormat;

            Subject.IsShared(GivenItem(1, "/media/scenes/Net/Studio", ItemType.Scene)).Should().BeTrue();
        }

        [Test]
        public void is_shared_true_when_another_item_has_same_path()
        {
            GivenEntries(Entry(1, "/media/scenes/Studio/A", ItemType.Scene), Entry(2, "/media/scenes/Studio/A/", ItemType.Scene));

            Subject.IsShared(GivenItem(1, "/media/scenes/Studio/A", ItemType.Scene)).Should().BeTrue();
        }

        [Test]
        public void is_shared_true_when_scene_path_is_ancestor_of_another_item()
        {
            GivenEntries(Entry(1, "/media/scenes/Studio", ItemType.Scene), Entry(2, "/media/scenes/Studio/B", ItemType.Scene));

            Subject.IsShared(GivenItem(1, "/media/scenes/Studio", ItemType.Scene)).Should().BeTrue();
        }

        [Test]
        public void is_shared_false_for_scene_alone_in_per_scene_folder()
        {
            GivenEntries(Entry(1, "/media/scenes/Studio/A", ItemType.Scene), Entry(2, "/media/scenes/Studio/B", ItemType.Scene), Entry(3, "", ItemType.Scene));

            Subject.IsShared(GivenItem(1, "/media/scenes/Studio/A", ItemType.Scene)).Should().BeFalse();
        }

        [Test]
        public void is_shared_false_for_scene_without_path()
        {
            GivenEntries(Entry(2, "/media/scenes/Studio/B", ItemType.Scene));

            Subject.IsShared(GivenItem(1, "", ItemType.Scene)).Should().BeFalse();
        }

        [Test]
        public void get_items_in_folder_returns_items_with_equal_path()
        {
            GivenEntries(Entry(1, "/media/scenes/Studio", ItemType.Scene), Entry(2, "/media/scenes/Studio/", ItemType.Scene), Entry(3, "/media/scenes/Studio/B", ItemType.Scene), Entry(4, null, ItemType.Scene));

            Subject.GetItemsInFolder("/media/scenes/Studio").Should().OnlyContain(e => e.Id == 1 || e.Id == 2);
        }

        [Test]
        public void path_cache_is_cleared_on_movie_edited_event()
        {
            GivenEntries(Entry(1, "/media/scenes/Studio/A", ItemType.Scene));

            Subject.GetItemsInFolder("/media/scenes/Studio/A");
            Subject.GetItemsInFolder("/media/scenes/Studio/A");

            Mocker.GetMock<IMovieRepository>().Verify(s => s.AllMoviePathEntries(), Times.Once());

            Subject.Handle(new MovieEditedEvent(new Movie(), new Movie()));
            Subject.GetItemsInFolder("/media/scenes/Studio/A");

            Mocker.GetMock<IMovieRepository>().Verify(s => s.AllMoviePathEntries(), Times.Exactly(2));
        }

        [Test]
        public void path_cache_is_cleared_on_movies_bulk_edited_event()
        {
            GivenEntries(Entry(1, "/media/scenes/Studio/A", ItemType.Scene));

            Subject.GetItemsInFolder("/media/scenes/Studio/A");
            Subject.GetItemsInFolder("/media/scenes/Studio/A");

            Mocker.GetMock<IMovieRepository>().Verify(s => s.AllMoviePathEntries(), Times.Once());

            Subject.Handle(new MoviesBulkEditedEvent(new List<Movie>()));
            Subject.GetItemsInFolder("/media/scenes/Studio/A");

            Mocker.GetMock<IMovieRepository>().Verify(s => s.AllMoviePathEntries(), Times.Exactly(2));
        }

        private void GivenEntries(params MoviePathEntry[] entries)
        {
            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.AllMoviePathEntries())
                  .Returns(new List<MoviePathEntry>(entries));
        }

        private static MoviePathEntry Entry(int id, string path, ItemType itemType)
        {
            return new MoviePathEntry { Id = id, Path = path, ItemType = itemType };
        }

        private static Movie GivenItem(int id, string path, ItemType itemType)
        {
            var movie = new Movie { Id = id, Path = path };
            movie.MovieMetadata.Value.ItemType = itemType;

            return movie;
        }
    }
}
