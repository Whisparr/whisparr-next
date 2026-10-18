using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.ImportExclusions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class SceneCatalogImporterFixture : CoreTest<SceneCatalogImporter>
    {
        private List<string> _libraryIds;
        private List<string> _performerIds;
        private List<ImportListExclusion> _exclusions;

        [SetUp]
        public void Setup()
        {
            _libraryIds = new List<string>();
            _performerIds = new List<string>();
            _exclusions = new List<ImportListExclusion>();

            Mocker.GetMock<IMovieService>().Setup(s => s.AllMovieStashIds()).Returns(() => _libraryIds);
            Mocker.GetMock<IPerformerService>().Setup(s => s.AllPerformerForeignIds()).Returns(() => _performerIds);
            Mocker.GetMock<IImportListExclusionService>().Setup(s => s.GetAllExclusions()).Returns(() => _exclusions);

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.PrefetchPerformers(It.IsAny<IEnumerable<string>>()))
                  .Returns(new List<string>());

            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), true, true))
                  .Returns<List<Movie>, bool, bool>((movies, _, _) => movies);
        }

        private static ScenePageItem Item(string id, params string[] performers)
        {
            return new ScenePageItem { StashId = id, PerformerStashIds = performers.ToList() };
        }

        private static ScenePage Page(ScenePageStatus status, int total, params ScenePageItem[] items)
        {
            return new ScenePage { Status = status, Total = total, Items = items.ToList() };
        }

        private void GivenPages(params ScenePage[] pages)
        {
            Mocker.GetMock<IScenePageWalker>()
                  .Setup(w => w.Walk(It.IsAny<Func<string, ScenePage>>(), It.IsAny<Action<ScenePage>>()))
                  .Returns<Func<string, ScenePage>, Action<ScenePage>>((_, handle) =>
                  {
                      foreach (var page in pages)
                      {
                          handle(page);
                      }

                      return new ScenePageWalkResult { Pages = pages.Length };
                  });
        }

        private int Import()
        {
            return Subject.ImportScenes("Studio", _ => null, id => new Movie { ForeignId = id });
        }

        private static List<string> Ids(List<Movie> movies) => movies.Select(m => m.ForeignId).ToList();

        [Test]
        public void should_add_new_scenes_page_by_page()
        {
            GivenPages(
                Page(ScenePageStatus.Complete, 4, Item("a"), Item("b")),
                Page(ScenePageStatus.Complete, 4, Item("c"), Item("d")));

            var added = new List<List<string>>();
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), true, true))
                  .Callback<List<Movie>, bool, bool>((movies, _, _) => added.Add(Ids(movies)))
                  .Returns<List<Movie>, bool, bool>((movies, _, _) => movies);

            var result = Import();

            result.Should().Be(4);
            added.Should().HaveCount(2);
            added[0].Should().Equal("a", "b");
            added[1].Should().Equal("c", "d");

            Mocker.GetMock<IRefreshNewScenes>().Verify(r => r.RefreshNewScenes(It.IsAny<List<Movie>>()), Times.Exactly(2));
            Mocker.GetMock<IRefreshNewScenes>().Verify(r => r.RefreshNewScenes(It.Is<List<Movie>>(m => Ids(m).SequenceEqual(new[] { "a", "b" }))), Times.Once());
            Mocker.GetMock<IRefreshNewScenes>().Verify(r => r.RefreshNewScenes(It.Is<List<Movie>>(m => Ids(m).SequenceEqual(new[] { "c", "d" }))), Times.Once());
        }

        [Test]
        public void should_skip_scenes_already_in_library_or_excluded()
        {
            _libraryIds.Add("a");
            _exclusions.Add(new ImportListExclusion { ForeignId = "b" });

            GivenPages(Page(ScenePageStatus.Complete, 3, Item("a", "pa"), Item("b", "pb"), Item("c", "pc")));

            var result = Import();

            result.Should().Be(1);
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.Is<List<Movie>>(m => Ids(m).SequenceEqual(new[] { "c" })), true, true), Times.Once());
            Mocker.GetMock<IProvideMovieInfo>().Verify(s => s.PrefetchPerformers(It.Is<IEnumerable<string>>(p => p.Contains("pa") || p.Contains("pb"))), Times.Never());
            Mocker.GetMock<IProvideMovieInfo>().Verify(s => s.PrefetchPerformers(It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { "pc" }))), Times.Once());
        }

        [Test]
        public void should_not_add_a_scene_twice_when_a_pending_page_is_revisited()
        {
            GivenPages(
                Page(ScenePageStatus.Pending, 2, Item("a")),
                Page(ScenePageStatus.Complete, 2, Item("a"), Item("b")));

            var added = new List<List<string>>();
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), true, true))
                  .Callback<List<Movie>, bool, bool>((movies, _, _) => added.Add(Ids(movies)))
                  .Returns<List<Movie>, bool, bool>((movies, _, _) => movies);

            var result = Import();

            result.Should().Be(2);
            added.Should().HaveCount(2);
            added[0].Should().Equal("a");
            added[1].Should().Equal("b");
        }

        [Test]
        public void should_prefetch_only_unknown_performers_before_adding()
        {
            _performerIds.Add("known");
            GivenPages(Page(ScenePageStatus.Complete, 2, Item("a", "known", "new1"), Item("b", "new1", "new2")));

            var calls = new List<string>();
            List<string> prefetched = null;

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.PrefetchPerformers(It.IsAny<IEnumerable<string>>()))
                  .Callback<IEnumerable<string>>(p =>
                  {
                      calls.Add("prefetch");
                      prefetched = p.ToList();
                  })
                  .Returns(new List<string>());
            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), true, true))
                  .Callback<List<Movie>, bool, bool>((_, _, _) => calls.Add("add"))
                  .Returns<List<Movie>, bool, bool>((movies, _, _) => movies);
            Mocker.GetMock<IRefreshNewScenes>()
                  .Setup(r => r.RefreshNewScenes(It.IsAny<List<Movie>>()))
                  .Callback<List<Movie>>(_ => calls.Add("refresh"));

            Import();

            calls.Should().Equal("prefetch", "add", "refresh");
            prefetched.Should().BeEquivalentTo(new[] { "new1", "new2" });
        }

        [Test]
        public void should_add_scenes_without_waiting_for_pending_performers()
        {
            GivenPages(Page(ScenePageStatus.Complete, 1, Item("a", "p1", "p2")));

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.PrefetchPerformers(It.IsAny<IEnumerable<string>>()))
                  .Returns<IEnumerable<string>>(p => p.ToList());

            var result = Import();

            result.Should().Be(1);
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.IsAny<List<Movie>>(), true, true), Times.Once());
        }

        [Test]
        public void should_keep_scenes_from_earlier_pages_when_a_later_page_fails()
        {
            Mocker.GetMock<IScenePageWalker>()
                  .Setup(w => w.Walk(It.IsAny<Func<string, ScenePage>>(), It.IsAny<Action<ScenePage>>()))
                  .Callback<Func<string, ScenePage>, Action<ScenePage>>((_, handle) =>
                  {
                      handle(Page(ScenePageStatus.Complete, 4, Item("a"), Item("b")));
                      throw new HttpException(new HttpResponse(new HttpRequest("http://skyhook.test"), new HttpHeader(), string.Empty, System.Net.HttpStatusCode.BadGateway));
                  });

            Action act = () => Import();

            act.Should().Throw<HttpException>();
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.Is<List<Movie>>(m => Ids(m).SequenceEqual(new[] { "a", "b" })), true, true), Times.Once());
        }

        [Test]
        public void should_warn_about_scenes_the_walker_gave_up_on()
        {
            Mocker.GetMock<IScenePageWalker>()
                  .Setup(w => w.Walk(It.IsAny<Func<string, ScenePage>>(), It.IsAny<Action<ScenePage>>()))
                  .Returns(new ScenePageWalkResult { Pages = 1, GaveUp = new List<string> { "x", "y" } });

            Import().Should().Be(0);

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
