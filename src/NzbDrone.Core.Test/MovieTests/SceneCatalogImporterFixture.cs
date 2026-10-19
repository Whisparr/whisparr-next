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
        private List<string> _sceneForeignIds;
        private List<string> _performerIds;
        private List<ImportListExclusion> _exclusions;
        private Func<bool> _keepGoing;

        [SetUp]
        public void Setup()
        {
            _performerIds = new List<string>();
            _exclusions = new List<ImportListExclusion>();
            _keepGoing = () => true;

            _sceneForeignIds = new List<string>();

            Mocker.GetMock<IMovieService>().Setup(s => s.AllSceneForeignIds()).Returns(() => _sceneForeignIds);
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
            return new ScenePageItem { SceneId = id, PerformerIds = performers.ToList() };
        }

        private static ScenePage Page(ScenePageStatus status, int total, params ScenePageItem[] items)
        {
            return new ScenePage { Status = status, Total = total, Items = items.ToList() };
        }

        private void GivenPages(params ScenePage[] pages)
        {
            Mocker.GetMock<IScenePageWalker>()
                  .Setup(w => w.Walk(It.IsAny<Func<string, ScenePage>>(), It.IsAny<Action<ScenePage>>(), It.IsAny<Func<bool>>()))
                  .Returns<Func<string, ScenePage>, Action<ScenePage>, Func<bool>>((_, handle, _) =>
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
            return Subject.ImportScenes("Studio", _ => null, id => new Movie { ForeignId = id }, () => _keepGoing());
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
            _sceneForeignIds.Add("a");
            _exclusions.Add(new ImportListExclusion { ForeignId = "b" });

            GivenPages(Page(ScenePageStatus.Complete, 3, Item("a", "pa"), Item("b", "pb"), Item("c", "pc")));

            var result = Import();

            result.Should().Be(1);
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.Is<List<Movie>>(m => Ids(m).SequenceEqual(new[] { "c" })), true, true), Times.Once());
            Mocker.GetMock<IProvideMovieInfo>().Verify(s => s.PrefetchPerformers(It.Is<IEnumerable<string>>(p => p.Contains("pa") || p.Contains("pb"))), Times.Never());
            Mocker.GetMock<IProvideMovieInfo>().Verify(s => s.PrefetchPerformers(It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { "pc" }))), Times.Once());
        }

        [Test]
        public void should_skip_scenes_already_in_library_when_source_is_tpdb()
        {
            var tpdbId = "3f2b1c9e-0000-4000-8000-000000000001";
            _sceneForeignIds.Add(tpdbId);

            GivenPages(Page(ScenePageStatus.Complete, 2, Item(tpdbId), Item("3f2b1c9e-0000-4000-8000-000000000002")));

            var result = Import();

            result.Should().Be(1);
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.Is<List<Movie>>(m => Ids(m).SequenceEqual(new[] { "3f2b1c9e-0000-4000-8000-000000000002" })), true, true), Times.Once());
        }

        [Test]
        public void should_import_undated_tpdb_scene()
        {
            GivenPages(Page(ScenePageStatus.Complete, 1, new ScenePageItem { SceneId = "undated", PerformerIds = new List<string>() }));

            // The page item carries no date; the stub has none either and is added as-is for the inline refresh to fill in.
            var result = Import();

            result.Should().Be(1);
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.Is<List<Movie>>(m => Ids(m).SequenceEqual(new[] { "undated" })), true, true), Times.Once());
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
                  .Setup(w => w.Walk(It.IsAny<Func<string, ScenePage>>(), It.IsAny<Action<ScenePage>>(), It.IsAny<Func<bool>>()))
                  .Callback<Func<string, ScenePage>, Action<ScenePage>, Func<bool>>((_, handle, _) =>
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
                  .Setup(w => w.Walk(It.IsAny<Func<string, ScenePage>>(), It.IsAny<Action<ScenePage>>(), It.IsAny<Func<bool>>()))
                  .Returns(new ScenePageWalkResult { Pages = 1, GaveUp = new List<string> { "x", "y" } });

            Import().Should().Be(0);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_evict_prefetched_items_and_performers_after_each_page()
        {
            _performerIds.Add("known");
            GivenPages(
                Page(ScenePageStatus.Complete, 3, Item("a", "known", "new1"), Item("b")),
                Page(ScenePageStatus.Complete, 3, Item("c", "new2")));

            var forgotten = new List<(List<string> Scenes, List<string> Performers)>();
            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.ForgetPrefetched(It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>()))
                  .Callback<IEnumerable<string>, IEnumerable<string>>((s, p) => forgotten.Add((s.ToList(), p.ToList())));

            Import();

            forgotten.Should().HaveCount(2);
            forgotten[0].Scenes.Should().Equal("a", "b");
            forgotten[0].Performers.Should().Equal("new1");
            forgotten[1].Scenes.Should().Equal("c");
            forgotten[1].Performers.Should().Equal("new2");
        }

        [Test]
        public void should_evict_prefetched_items_even_when_the_page_has_nothing_new()
        {
            _sceneForeignIds.Add("a");
            GivenPages(Page(ScenePageStatus.Complete, 1, Item("a")));

            Import();

            Mocker.GetMock<IProvideMovieInfo>().Verify(s => s.ForgetPrefetched(It.Is<IEnumerable<string>>(i => i.SequenceEqual(new[] { "a" })), It.IsAny<IEnumerable<string>>()), Times.Once());
        }

        [Test]
        public void should_evict_prefetched_items_when_adding_throws()
        {
            GivenPages(Page(ScenePageStatus.Complete, 1, Item("a", "p1")));

            Mocker.GetMock<IAddMovieService>()
                  .Setup(s => s.AddMovies(It.IsAny<List<Movie>>(), true, true))
                  .Throws(new InvalidOperationException());

            Action act = () => Import();

            act.Should().Throw<InvalidOperationException>();
            Mocker.GetMock<IProvideMovieInfo>().Verify(
                s => s.ForgetPrefetched(It.Is<IEnumerable<string>>(i => i.SequenceEqual(new[] { "a" })), It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { "p1" }))),
                Times.Once());
        }

        [Test]
        public void should_not_add_a_fetched_page_once_told_to_stop()
        {
            var pages = 0;
            _keepGoing = () => pages++ < 1;

            GivenPages(
                Page(ScenePageStatus.Complete, 2, Item("a")),
                Page(ScenePageStatus.Complete, 2, Item("b")));

            Import().Should().Be(1);

            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.Is<List<Movie>>(m => Ids(m).SequenceEqual(new[] { "a" })), true, true), Times.Once());
            Mocker.GetMock<IAddMovieService>().Verify(s => s.AddMovies(It.Is<List<Movie>>(m => Ids(m).Contains("b")), true, true), Times.Never());
        }

        [Test]
        public void should_pass_the_keep_going_check_to_the_walker_and_stop_quietly()
        {
            Func<bool> passed = null;
            Mocker.GetMock<IScenePageWalker>()
                  .Setup(w => w.Walk(It.IsAny<Func<string, ScenePage>>(), It.IsAny<Action<ScenePage>>(), It.IsAny<Func<bool>>()))
                  .Callback<Func<string, ScenePage>, Action<ScenePage>, Func<bool>>((_, _, keep) => passed = keep)
                  .Returns(new ScenePageWalkResult { Stopped = true });

            _keepGoing = () => false;

            Import().Should().Be(0);

            passed().Should().BeFalse();
        }

        [Test]
        public void should_tolerate_a_page_without_items()
        {
            GivenPages(new ScenePage { Status = ScenePageStatus.Complete, Items = null });

            Import().Should().Be(0);
        }
    }
}
