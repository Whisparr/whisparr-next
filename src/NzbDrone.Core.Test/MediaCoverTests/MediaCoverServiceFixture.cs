using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.LegacyMigration.Events;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Performers.Events;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Movies.Studios.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class MediaCoverServiceFixture : CoreTest<MediaCoverService>
    {
        private const string RemoteUrl = "https://stashdb.org/images/e9be2754-f9de-4db7-8cac-a64afd2a5126";

        private Movie _movie;

        // The remote URL's hash, truncated the same way MediaCoverService truncates it.
        private static string ExpectedHash => RemoteUrl.SHA256Hash()[..20];

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IAppFolderInfo>(new AppFolderInfo(Mocker.Resolve<IStartupContext>()));

            _movie = Builder<Movie>.CreateNew()
                .With(v => v.Id = 2)
                .With(v => v.MovieMetadata.Value.Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Poster, "") })
                .Build();

            Mocker.GetMock<IMovieService>().Setup(m => m.GetMovie(It.Is<int>(id => id == _movie.Id))).Returns(_movie);
        }

        [TearDown]
        public void TearDown()
        {
            (Subject as IHandle<ApplicationShutdownRequested>)?.Handle(new ApplicationShutdownRequested());
        }

        [TestCase("https://cdn.theporndb.net/scene/8e/52/e6/abc/background/c/bg-molly-i-love-it-large.webp", "https://cdn.theporndb.net/scene/8e/52/e6/abc/background/bg-molly-i-love-it.webp")]
        [TestCase("https://cdn.theporndb.net/scene/1/background/c/bg-x-medium.JPG", "https://cdn.theporndb.net/scene/1/background/bg-x.JPG")]
        public void should_map_a_resized_image_url_to_the_original(string url, string original)
        {
            MediaCoverService.TryGetOriginalImageUrl(url, out var originalUrl).Should().BeTrue();
            originalUrl.Should().Be(original);
        }

        [TestCase("https://cdn.theporndb.net/scene/1/background/bg-x.webp")]
        [TestCase("https://cdn.theporndb.net/scene/1/background/c/bg-x-thumb.webp")]
        [TestCase("https://image.tmdb.org/t/p/original/c/abc-large.jpg")]
        [TestCase(null)]
        public void should_not_map_an_image_url_that_is_not_a_resized_copy(string url)
        {
            MediaCoverService.TryGetOriginalImageUrl(url, out var originalUrl).Should().BeFalse();
            originalUrl.Should().BeNull();
        }

        [Test]
        public void should_convert_cover_urls_to_local()
        {
            var covers = GivenCovers();

            Subject.ConvertToLocalUrls(12, covers);

            covers.Single().Url.Should().Be($"/MediaCover/movie/12/banner.jpg?h={ExpectedHash}");
        }

        [Test]
        public void should_convert_performer_cover_urls_to_local()
        {
            var covers = GivenCovers();

            Subject.ConvertToLocalPerformerUrls(12, covers);

            covers.Single().Url.Should().Be($"/MediaCover/performer/12/banner.jpg?h={ExpectedHash}");
        }

        [Test]
        public void should_convert_studio_cover_urls_to_local()
        {
            var covers = GivenCovers();

            Subject.ConvertToLocalStudioUrls(12, covers);

            covers.Single().Url.Should().Be($"/MediaCover/studio/12/banner.jpg?h={ExpectedHash}");
        }

        [Test]
        public void should_convert_media_urls_to_local_without_a_hash_when_there_is_no_remote_url()
        {
            var covers = GivenCovers(null);

            Subject.ConvertToLocalUrls(12, covers);

            covers.Single().Url.Should().Be("/MediaCover/movie/12/banner.jpg");
        }

        [Test]
        public void should_not_touch_disk_to_build_movie_cover_urls()
        {
            Subject.ConvertToLocalUrls(12, GivenCovers());

            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFileInfo(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FileGetLastWrite(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_main_and_resized_movie_covers_for_the_given_types()
        {
            var expected = new[]
            {
                Subject.GetMovieCoverPath(12, MediaCoverTypes.Poster),
                Subject.GetMovieCoverPath(12, MediaCoverTypes.Poster) + ".source",
                Subject.GetMovieCoverPath(12, MediaCoverTypes.Poster, 500),
                Subject.GetMovieCoverPath(12, MediaCoverTypes.Poster, 250),
                Subject.GetMovieCoverPath(12, MediaCoverTypes.Fanart),
                Subject.GetMovieCoverPath(12, MediaCoverTypes.Fanart) + ".source",
                Subject.GetMovieCoverPath(12, MediaCoverTypes.Fanart, 360),
                Subject.GetMovieCoverPath(12, MediaCoverTypes.Fanart, 180)
            };

            Mocker.GetMock<IDiskProvider>().Setup(v => v.FileExists(It.IsAny<string>())).Returns(true);

            Subject.DeleteMovieCovers(12, new[] { MediaCoverTypes.Poster, MediaCoverTypes.Fanart });

            foreach (var path in expected)
            {
                Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(path), Times.Once());
            }

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Exactly(expected.Length));
        }

        [Test]
        public void should_skip_missing_files_and_other_cover_types_when_deleting_movie_covers()
        {
            Mocker.GetMock<IDiskProvider>().Setup(v => v.FileExists(It.IsAny<string>())).Returns(false);

            Subject.DeleteMovieCovers(12, new[] { MediaCoverTypes.Poster });

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_only_delete_the_requested_cover_type()
        {
            Mocker.GetMock<IDiskProvider>().Setup(v => v.FileExists(It.IsAny<string>())).Returns(true);

            Subject.DeleteMovieCovers(12, new[] { MediaCoverTypes.Poster });

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(Subject.GetMovieCoverPath(12, MediaCoverTypes.Fanart)), Times.Never());
        }

        [Test]
        public void should_not_create_the_studio_folder_while_building_urls()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FolderExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ConvertToLocalStudioUrls(12, GivenCovers());

            Mocker.GetMock<IDiskProvider>().Verify(v => v.CreateFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_give_two_covers_of_the_same_type_distinct_urls()
        {
            var covers = new List<MediaCover.MediaCover>
            {
                new () { CoverType = MediaCoverTypes.Screenshot, RemoteUrl = "https://stashdb.org/images/bac219ec-204e-4d7c-8909-5f258d7ed218" },
                new () { CoverType = MediaCoverTypes.Screenshot, RemoteUrl = "https://stashdb.org/images/6b073398-6ffc-4690-8bfd-ae160ecc214e" }
            };

            Subject.ConvertToLocalUrls(12, covers);

            covers[0].Url.Should().NotBe(covers[1].Url);
        }

        [Test]
        public void should_convert_cover_urls_to_local_without_a_hash_when_the_cover_has_not_been_downloaded()
        {
            var covers = GivenCovers();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ConvertToLocalUrls(12, covers, DateTime.UtcNow);

            covers.Single().Url.Should().Be("/MediaCover/movie/12/banner.jpg");
        }

        [Test]
        public void should_convert_cover_urls_to_local_with_a_hash_once_the_cover_is_on_disk()
        {
            var covers = GivenCovers();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.ConvertToLocalUrls(12, covers, DateTime.UtcNow);

            covers.Single().Url.Should().Be($"/MediaCover/movie/12/banner.jpg?h={ExpectedHash}");
        }

        [Test]
        public void should_only_check_whether_a_cover_exists_once()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.ConvertToLocalUrls(12, GivenCovers(), DateTime.UtcNow);
            Subject.ConvertToLocalUrls(12, GivenCovers(), DateTime.UtcNow);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.FileExists(It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_not_check_whether_a_cover_exists_for_an_item_added_more_than_a_day_ago()
        {
            var covers = GivenCovers();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ConvertToLocalUrls(12, covers, DateTime.UtcNow.AddDays(-2));

            covers.Single().Url.Should().Be($"/MediaCover/movie/12/banner.jpg?h={ExpectedHash}");
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FileExists(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_check_whether_a_cover_exists_when_the_caller_does_not_know_when_it_was_added()
        {
            var covers = GivenCovers();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ConvertToLocalUrls(12, covers);

            covers.Single().Url.Should().Be($"/MediaCover/movie/12/banner.jpg?h={ExpectedHash}");
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FileExists(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_convert_performer_cover_urls_to_local_without_a_hash_when_the_cover_has_not_been_downloaded()
        {
            var covers = GivenCovers();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.ConvertToLocalPerformerUrls(12, covers, DateTime.UtcNow);

            covers.Single().Url.Should().Be("/MediaCover/performer/12/banner.jpg");
        }

        [Test]
        public void should_change_the_url_when_the_remote_url_changes()
        {
            var before = GivenCovers();
            var after = GivenCovers("https://stashdb.org/images/6b073398-6ffc-4690-8bfd-ae160ecc214e");

            Subject.ConvertToLocalUrls(12, before);
            Subject.ConvertToLocalUrls(12, after);

            after.Single().Url.Should().NotBe(before.Single().Url);
        }

        [Test]
        public void should_queue_movie_updates_without_blocking_event_intake()
        {
            var coverStarted = new ManualResetEventSlim();
            var releaseCover = new ManualResetEventSlim();
            var handler = Subject as IHandle<MovieUpdatedEvent>;
            var lifecycle = Subject as IHandle<ApplicationStartedEvent>;

            handler.Should().NotBeNull();
            lifecycle.Should().NotBeNull();

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback(() =>
                  {
                      coverStarted.Set();
                      releaseCover.Wait();
                  })
                  .Returns(true);

            lifecycle.Handle(new ApplicationStartedEvent());

            var intake = Task.Run(() => handler.Handle(new MovieUpdatedEvent(_movie)));

            try
            {
                coverStarted.Wait(1000).Should().BeTrue();
                intake.Wait(1000).Should().BeTrue();
            }
            finally
            {
                releaseCover.Set();
            }
        }

        [Test]
        public void should_coalesce_pending_movie_updates_and_process_the_latest_movie()
        {
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            var workersStarted = new CountdownEvent(workerCount);
            var completed = new CountdownEvent(workerCount + 1);
            var releaseCovers = new ManualResetEventSlim();
            var processedUrls = new List<string>();
            var startedCalls = 0;
            var handler = (IHandle<MovieUpdatedEvent>)Subject;

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((remoteUrl, _) =>
                  {
                      lock (processedUrls)
                      {
                          processedUrls.Add(remoteUrl);
                      }

                      if (Interlocked.Increment(ref startedCalls) <= workerCount)
                      {
                          workersStarted.Signal();
                          releaseCovers.Wait();
                      }
                  })
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);

            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback(() => completed.Signal());

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < workerCount; i++)
            {
                handler.Handle(new MovieUpdatedEvent(GivenMovie(1000 + i, $"https://example.com/block-{i}")));
            }

            workersStarted.Wait(5000).Should().BeTrue();

            handler.Handle(new MovieUpdatedEvent(GivenMovie(2000, "https://example.com/old")));
            handler.Handle(new MovieUpdatedEvent(GivenMovie(2000, "https://example.com/latest")));
            releaseCovers.Set();

            completed.Wait(15000).Should().BeTrue();

            lock (processedUrls)
            {
                processedUrls.Should().ContainSingle(v => v == "https://example.com/latest");
                processedUrls.Should().NotContain(v => v == "https://example.com/old");
            }
        }

        [Test]
        public void should_preserve_handler_entry_order_when_full_queue_waiters_resume_in_reverse()
        {
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            var workersStarted = new CountdownEvent(workerCount);
            var releaseInitialWorkers = new ManualResetEventSlim();
            var releaseSecondWave = new ManualResetEventSlim();
            var oldWaiterAcquiredSlot = new ManualResetEventSlim();
            var releaseOldWaiter = new ManualResetEventSlim();
            var newestCompleted = new ManualResetEventSlim();
            var completedUrl = string.Empty;

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((remoteUrl, _) =>
                  {
                      if (remoteUrl.StartsWith("https://example.com/initial-block-"))
                      {
                          workersStarted.Signal();
                          releaseInitialWorkers.Wait();
                      }
                      else if (remoteUrl.StartsWith("https://example.com/second-block-"))
                      {
                          releaseSecondWave.Wait();
                      }
                  })
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      if (message.Movie.Id == 9000)
                      {
                          completedUrl = message.Movie.MovieMetadata.Value.Images.Single().RemoteUrl;
                          newestCompleted.Set();
                      }
                  });

            Subject.MovieCoverQueueTest.SlotAcquired = (movie, _) =>
            {
                if (movie.MovieMetadata.Value.Images.Single().RemoteUrl == "https://example.com/old-waiter")
                {
                    oldWaiterAcquiredSlot.Set();
                    releaseOldWaiter.Wait();
                }
            };

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < workerCount; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(1000 + i, $"https://example.com/initial-block-{i}")));
            }

            workersStarted.Wait(5000).Should().BeTrue();

            for (var i = 0; i < MediaCoverService.MovieCoverQueueCapacity; i++)
            {
                var url = i < workerCount ? $"https://example.com/second-block-{i}" : $"https://example.com/queued-{i}";
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(2000 + i, url)));
            }

            var older = Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(9000, "https://example.com/old-waiter"))));
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.BlockedProducerCount == 1, 5000).Should().BeTrue();
            var newer = Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(9000, "https://example.com/new-waiter"))));
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.BlockedProducerCount == 2, 5000).Should().BeTrue();

            releaseInitialWorkers.Set();
            oldWaiterAcquiredSlot.Wait(5000).Should().BeTrue();
            releaseSecondWave.Set();
            newer.Wait(5000).Should().BeTrue();
            releaseOldWaiter.Set();
            older.Wait(5000).Should().BeTrue();

            newestCompleted.Wait(15000).Should().BeTrue();
            completedUrl.Should().Be("https://example.com/new-waiter");
        }

        [Test]
        public void should_skip_an_older_producer_after_the_latest_update_has_been_dequeued()
        {
            const int movieId = 12000;
            var oldProducerAcquiredSlot = new ManualResetEventSlim();
            var releaseOldProducer = new ManualResetEventSlim();
            var latestCompleted = new ManualResetEventSlim();
            var oldCompleted = new ManualResetEventSlim();
            var completions = new ConcurrentQueue<string>();

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      if (message.Movie.Id == movieId)
                      {
                          var remoteUrl = message.Movie.MovieMetadata.Value.Images.Single().RemoteUrl;
                          completions.Enqueue(remoteUrl);

                          if (remoteUrl == "https://example.com/latest")
                          {
                              latestCompleted.Set();
                          }
                          else
                          {
                              oldCompleted.Set();
                          }
                      }
                  });

            Subject.MovieCoverQueueTest.SlotAcquired = (movie, _) =>
            {
                if (movie.Id == movieId && movie.MovieMetadata.Value.Images.Single().RemoteUrl == "https://example.com/old")
                {
                    oldProducerAcquiredSlot.Set();
                    releaseOldProducer.Wait();
                }
            };

            Subject.Handle(new ApplicationStartedEvent());
            var oldProducer = Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(movieId, "https://example.com/old"))));
            oldProducerAcquiredSlot.Wait(5000).Should().BeTrue();

            Subject.Handle(new MovieUpdatedEvent(GivenMovie(movieId, "https://example.com/latest")));
            latestCompleted.Wait(5000).Should().BeTrue();

            releaseOldProducer.Set();
            oldProducer.Wait(5000).Should().BeTrue();
            oldCompleted.Wait(200).Should().BeFalse();

            completions.Should().Equal("https://example.com/latest");
        }

        [Test]
        public void should_drop_a_queued_movie_cover_when_the_movie_is_deleted()
        {
            const int deletedMovieId = 12200;
            const int survivorMovieId = 12201;
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            var workersStarted = new CountdownEvent(workerCount);
            var releaseWorkers = new ManualResetEventSlim();
            var survivorCompleted = new ManualResetEventSlim();
            var deletedMovieProcessed = new ManualResetEventSlim();
            var deletedMovie = GivenMovie(deletedMovieId, "https://example.com/deleted");

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((remoteUrl, _) =>
                  {
                      if (remoteUrl.StartsWith("https://example.com/delete-block-"))
                      {
                          workersStarted.Signal();
                          releaseWorkers.Wait();
                      }
                      else if (remoteUrl == "https://example.com/deleted")
                      {
                          deletedMovieProcessed.Set();
                      }
                  })
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.GetFileSize(It.IsAny<string>())).Returns(1000);
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      if (message.Movie.Id == survivorMovieId)
                      {
                          survivorCompleted.Set();
                      }
                  });

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < workerCount; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(12100 + i, $"https://example.com/delete-block-{i}")));
            }

            workersStarted.Wait(5000).Should().BeTrue();
            Subject.Handle(new MovieUpdatedEvent(deletedMovie));
            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(1);

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { deletedMovie }, false, false));
            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(0);
            Subject.MovieCoverQueueTest.HasHighWaterSequence(deletedMovieId).Should().BeFalse();

            releaseWorkers.Set();
            Subject.Handle(new MovieUpdatedEvent(GivenMovie(survivorMovieId, "https://example.com/survivor")));

            survivorCompleted.Wait(5000).Should().BeTrue();
            deletedMovieProcessed.IsSet.Should().BeFalse();
        }

        [Test]
        public void should_restore_queue_capacity_after_multiple_queued_movies_are_deleted()
        {
            const int deletedMovieId = 12500;
            const int replacementMovieId = 12600;
            const int deletedMovieCount = 3;
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            var workersStarted = new CountdownEvent(workerCount);
            var releaseWorkers = new ManualResetEventSlim();

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((remoteUrl, _) =>
                  {
                      if (remoteUrl.StartsWith("https://example.com/capacity-block-"))
                      {
                          workersStarted.Signal();
                          releaseWorkers.Wait();
                      }
                  })
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.GetFileSize(It.IsAny<string>())).Returns(1000);

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < workerCount; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(12000 + i, $"https://example.com/capacity-block-{i}")));
            }

            workersStarted.Wait(5000).Should().BeTrue();

            try
            {
                for (var i = 0; i < MediaCoverService.MovieCoverQueueCapacity; i++)
                {
                    Subject.Handle(new MovieUpdatedEvent(GivenMovie(deletedMovieId + i)));
                }

                Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(MediaCoverService.MovieCoverQueueCapacity);
                var deletedMovies = Enumerable.Range(0, deletedMovieCount).Select(i => GivenMovie(deletedMovieId + i)).ToList();
                Subject.HandleAsync(new MoviesDeletedEvent(deletedMovies, false, false));
                Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(MediaCoverService.MovieCoverQueueCapacity - deletedMovieCount);

                var replacements = Enumerable.Range(0, deletedMovieCount)
                                             .Select(i => Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(replacementMovieId + i)))))
                                             .ToArray();

                Task.WaitAll(replacements, 5000).Should().BeTrue();
                Subject.MovieCoverQueueTest.BlockedProducerCount.Should().Be(0);
                Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(MediaCoverService.MovieCoverQueueCapacity);
            }
            finally
            {
                releaseWorkers.Set();
            }
        }

        [Test]
        public void should_reject_a_pre_deletion_producer_after_it_acquires_capacity()
        {
            const int deletedMovieId = 12300;
            const int survivorMovieId = 12301;
            var producerAcquiredSlot = new ManualResetEventSlim();
            var releaseProducer = new ManualResetEventSlim();
            var survivorCompleted = new ManualResetEventSlim();
            var deletedMovieProcessed = new ManualResetEventSlim();
            var deletedMovie = GivenMovie(deletedMovieId, "https://example.com/deleted-in-flight");

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((remoteUrl, _) =>
                  {
                      if (remoteUrl.StartsWith("https://example.com/deleted-in-flight"))
                      {
                          deletedMovieProcessed.Set();
                      }
                  })
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.GetFileSize(It.IsAny<string>())).Returns(1000);
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      if (message.Movie.Id == survivorMovieId)
                      {
                          survivorCompleted.Set();
                      }
                  });
            Subject.MovieCoverQueueTest.SlotAcquired = (movie, _) =>
            {
                if (movie.Id == deletedMovieId)
                {
                    producerAcquiredSlot.Set();
                    releaseProducer.Wait();
                }
            };

            Subject.Handle(new ApplicationStartedEvent());
            var producer = Task.Run(() => Subject.Handle(new MovieUpdatedEvent(deletedMovie)));
            producerAcquiredSlot.Wait(5000).Should().BeTrue();

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { deletedMovie }, false, false));
            Subject.Handle(new MovieUpdatedEvent(GivenMovie(deletedMovieId, "https://example.com/deleted-in-flight-after-delete")));
            releaseProducer.Set();
            producer.Wait(5000).Should().BeTrue();

            Subject.Handle(new MovieUpdatedEvent(GivenMovie(survivorMovieId, "https://example.com/survivor-after-delete")));

            survivorCompleted.Wait(5000).Should().BeTrue();
            deletedMovieProcessed.IsSet.Should().BeFalse();
            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(0);
            Subject.MovieCoverQueueTest.HasHighWaterSequence(deletedMovieId).Should().BeFalse();
        }

        [Test]
        public void should_remove_covers_recreated_by_active_work_after_movie_deletion()
        {
            const int deletedMovieId = 12400;
            var coverStarted = new ManualResetEventSlim();
            var releaseCover = new ManualResetEventSlim();
            var coverCompleted = new ManualResetEventSlim();
            var folderPresent = 1;
            var deleteCalls = 0;
            var deletedMovie = GivenMovie(deletedMovieId, "https://example.com/deleted-active");

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback(() =>
                  {
                      coverStarted.Set();
                      releaseCover.Wait();
                      Volatile.Write(ref folderPresent, 1);
                  })
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FolderExists(It.IsAny<string>()))
                  .Returns(() => Volatile.Read(ref folderPresent) == 1);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.DeleteFolder(It.IsAny<string>(), true))
                  .Callback(() =>
                  {
                      Volatile.Write(ref folderPresent, 0);
                      Interlocked.Increment(ref deleteCalls);
                  });
            Mocker.GetMock<IDiskProvider>().Setup(v => v.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.GetFileSize(It.IsAny<string>())).Returns(1000);
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback(coverCompleted.Set);

            Subject.Handle(new ApplicationStartedEvent());
            Subject.Handle(new MovieUpdatedEvent(deletedMovie));
            coverStarted.Wait(5000).Should().BeTrue();

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { deletedMovie }, false, false));
            Volatile.Read(ref folderPresent).Should().Be(0);
            releaseCover.Set();

            coverCompleted.Wait(5000).Should().BeTrue();
            SpinWait.SpinUntil(() => Volatile.Read(ref deleteCalls) >= 2, 5000).Should().BeTrue();
            Volatile.Read(ref folderPresent).Should().Be(0);
            SpinWait.SpinUntil(() => !Subject.MovieCoverQueueTest.HasHighWaterSequence(deletedMovieId), 5000).Should().BeTrue();
        }

        [Test]
        public void should_isolate_slot_hook_failures_without_losing_the_movie_cover_update()
        {
            var completed = new ManualResetEventSlim();

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback(completed.Set);
            Subject.MovieCoverQueueTest.SlotAcquired = (_, _) => throw new InvalidOperationException("synthetic slot hook failure");

            Subject.Handle(new ApplicationStartedEvent());

            var intake = () => Subject.Handle(new MovieUpdatedEvent(GivenMovie(12100)));
            intake.Should().NotThrow();
            completed.Wait(5000).Should().BeTrue();
            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(0);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_bound_and_complete_a_large_duplicate_heavy_update_stream()
        {
            const int uniquePendingMovies = 100;
            const int updateCount = 10000;
            const int firstPendingMovieId = 2000;
            var updateRounds = updateCount / uniquePendingMovies;
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            var workersStarted = new CountdownEvent(workerCount);
            var completed = new CountdownEvent(workerCount + uniquePendingMovies);
            var releaseCovers = new ManualResetEventSlim();
            var completionCounts = new ConcurrentDictionary<int, int>();
            var completedUrls = new ConcurrentDictionary<int, string>();
            var expectedUrls = new Dictionary<int, string>();
            var startedCalls = 0;
            var handler = (IHandle<MovieUpdatedEvent>)Subject;

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback(() =>
                  {
                      if (Interlocked.Increment(ref startedCalls) <= workerCount)
                      {
                          workersStarted.Signal();
                          releaseCovers.Wait();
                      }
                  })
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);

            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      completionCounts.AddOrUpdate(message.Movie.Id, 1, (_, count) => count + 1);
                      completedUrls[message.Movie.Id] = message.Movie.MovieMetadata.Value.Images.Single().RemoteUrl;
                      completed.Signal();
                  });

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < workerCount; i++)
            {
                handler.Handle(new MovieUpdatedEvent(GivenMovie(1000 + i)));
            }

            workersStarted.Wait(5000).Should().BeTrue();

            for (var round = 0; round < updateRounds; round++)
            {
                for (var movieOffset = 0; movieOffset < uniquePendingMovies; movieOffset++)
                {
                    var movieId = firstPendingMovieId + movieOffset;
                    var remoteUrl = $"https://example.com/movie-{movieId}/version-{round}";
                    expectedUrls[movieId] = remoteUrl;
                    handler.Handle(new MovieUpdatedEvent(GivenMovie(movieId, remoteUrl)));
                }

                Subject.MovieCoverQueueTest.PendingUniqueCount.Should().BeLessThanOrEqualTo(MediaCoverService.MovieCoverQueueCapacity);
            }

            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(uniquePendingMovies);
            releaseCovers.Set();
            completed.Wait(15000).Should().BeTrue();

            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(0);
            var pendingCompletionCounts = completionCounts.Where(v => v.Key >= firstPendingMovieId).ToDictionary(v => v.Key, v => v.Value);
            pendingCompletionCounts.Should().HaveCount(uniquePendingMovies);
            pendingCompletionCounts.Should().OnlyContain(v => v.Value == 1);

            foreach (var expected in expectedUrls)
            {
                completedUrls.Should().ContainKey(expected.Key).WhoseValue.Should().Be(expected.Value);
            }
        }

        [Test]
        public void should_apply_backpressure_when_the_movie_cover_queue_is_full()
        {
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            var totalUpdates = workerCount + MediaCoverService.MovieCoverQueueCapacity + 1;
            var workersStarted = new CountdownEvent(workerCount);
            var completed = new CountdownEvent(totalUpdates);
            var releaseCovers = new ManualResetEventSlim();
            var startedCalls = 0;
            var activeWorkers = 0;
            var maximumActiveWorkers = 0;
            var handler = (IHandle<MovieUpdatedEvent>)Subject;

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback(() =>
                  {
                      var active = Interlocked.Increment(ref activeWorkers);
                      var observedMaximum = maximumActiveWorkers;

                      while (active > observedMaximum)
                      {
                          var previous = Interlocked.CompareExchange(ref maximumActiveWorkers, active, observedMaximum);
                          if (previous == observedMaximum)
                          {
                              break;
                          }

                          observedMaximum = previous;
                      }

                      if (Interlocked.Increment(ref startedCalls) <= workerCount)
                      {
                          workersStarted.Signal();
                      }

                      releaseCovers.Wait();
                      Interlocked.Decrement(ref activeWorkers);
                  })
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);

            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback(() => completed.Signal());

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < workerCount; i++)
            {
                handler.Handle(new MovieUpdatedEvent(GivenMovie(1000 + i)));
            }

            workersStarted.Wait(5000).Should().BeTrue();

            for (var i = 0; i < MediaCoverService.MovieCoverQueueCapacity; i++)
            {
                handler.Handle(new MovieUpdatedEvent(GivenMovie(2000 + i)));
                Subject.MovieCoverQueueTest.PendingUniqueCount.Should().BeLessThanOrEqualTo(MediaCoverService.MovieCoverQueueCapacity);
            }

            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(MediaCoverService.MovieCoverQueueCapacity);
            var overflowIntake = Task.Run(() => handler.Handle(new MovieUpdatedEvent(GivenMovie(3000))));

            try
            {
                overflowIntake.Wait(200).Should().BeFalse();
            }
            finally
            {
                releaseCovers.Set();
            }

            overflowIntake.Wait(5000).Should().BeTrue();
            completed.Wait(15000).Should().BeTrue();
            maximumActiveWorkers.Should().BeLessThanOrEqualTo(workerCount);
        }

        [Test]
        public void should_release_multiple_overflow_producers_and_recover_capacity_during_normal_processing()
        {
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            var firstWorkersStarted = new CountdownEvent(workerCount);
            var releaseFirstWorkers = new ManualResetEventSlim();
            var firstWaveCompleted = new CountdownEvent(workerCount + MediaCoverService.MovieCoverQueueCapacity + 2);
            var recoveryWorkersStarted = new CountdownEvent(workerCount);
            var releaseRecoveryWorkers = new ManualResetEventSlim();
            var recoveryWaveCompleted = new CountdownEvent(workerCount + MediaCoverService.MovieCoverQueueCapacity);
            var completionCounts = new ConcurrentDictionary<int, int>();
            var completedUrls = new ConcurrentDictionary<int, string>();

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((remoteUrl, _) =>
                  {
                      if (remoteUrl.StartsWith("https://example.com/overflow-block-"))
                      {
                          firstWorkersStarted.Signal();
                          releaseFirstWorkers.Wait();
                      }
                      else if (remoteUrl.StartsWith("https://example.com/recovery-block-"))
                      {
                          recoveryWorkersStarted.Signal();
                          releaseRecoveryWorkers.Wait();
                      }
                  })
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      completionCounts.AddOrUpdate(message.Movie.Id, 1, (_, count) => count + 1);
                      completedUrls[message.Movie.Id] = message.Movie.MovieMetadata.Value.Images.Single().RemoteUrl;

                      if (message.Movie.Id >= 10000 && message.Movie.Id < 13000)
                      {
                          firstWaveCompleted.Signal();
                      }
                      else if (message.Movie.Id >= 13000 && message.Movie.Id < 15000)
                      {
                          recoveryWaveCompleted.Signal();
                      }
                  });

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < workerCount; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(10000 + i, $"https://example.com/overflow-block-{i}")));
            }

            firstWorkersStarted.Wait(5000).Should().BeTrue();

            for (var i = 0; i < MediaCoverService.MovieCoverQueueCapacity; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(11000 + i, $"https://example.com/overflow-queued-{i}")));
                Subject.MovieCoverQueueTest.PendingUniqueCount.Should().BeLessThanOrEqualTo(MediaCoverService.MovieCoverQueueCapacity);
            }

            var overflow = new List<Task>
            {
                Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(12000, "https://example.com/overflow-duplicate-old"))))
            };
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.BlockedProducerCount == 1, 5000).Should().BeTrue();
            overflow.Add(Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(12001, "https://example.com/overflow-unique")))));
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.BlockedProducerCount == 2, 5000).Should().BeTrue();
            overflow.Add(Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(12000, "https://example.com/overflow-duplicate-latest")))));
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.BlockedProducerCount == 3, 5000).Should().BeTrue();

            overflow.Should().OnlyContain(task => !task.IsCompleted);
            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(MediaCoverService.MovieCoverQueueCapacity);

            releaseFirstWorkers.Set();
            Task.WaitAll(overflow.ToArray(), 5000).Should().BeTrue();
            firstWaveCompleted.Wait(15000).Should().BeTrue();

            Subject.MovieCoverQueueTest.BlockedProducerCount.Should().Be(0);
            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(0);
            completionCounts.Should().ContainKey(12000).WhoseValue.Should().Be(1);
            completionCounts.Should().ContainKey(12001).WhoseValue.Should().Be(1);
            completedUrls.Should().ContainKey(12000).WhoseValue.Should().Be("https://example.com/overflow-duplicate-latest");

            for (var i = 0; i < workerCount; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(13000 + i, $"https://example.com/recovery-block-{i}")));
            }

            recoveryWorkersStarted.Wait(5000).Should().BeTrue();

            for (var i = 0; i < MediaCoverService.MovieCoverQueueCapacity; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(14000 + i, $"https://example.com/recovery-queued-{i}")));
                Subject.MovieCoverQueueTest.PendingUniqueCount.Should().BeLessThanOrEqualTo(MediaCoverService.MovieCoverQueueCapacity);
            }

            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(MediaCoverService.MovieCoverQueueCapacity);
            releaseRecoveryWorkers.Set();
            recoveryWaveCompleted.Wait(15000).Should().BeTrue();
            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(0);

            Subject.Handle(new ApplicationShutdownRequested());
            Subject.MovieCoverQueueTest.WorkerPoolSize.Should().Be(0);
        }

        [Test]
        public void should_return_within_one_overall_deadline_when_a_movie_cover_worker_is_stuck()
        {
            var coverStarted = new ManualResetEventSlim();
            var releaseCover = new ManualResetEventSlim();
            Subject.MovieCoverQueueTest.ShutdownTimeout = TimeSpan.FromMilliseconds(200);

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback(() =>
                  {
                      coverStarted.Set();
                      releaseCover.Wait();
                  })
                  .Returns(true);

            Subject.Handle(new ApplicationStartedEvent());
            Subject.Handle(new MovieUpdatedEvent(GivenMovie(3900)));
            coverStarted.Wait(5000).Should().BeTrue();

            try
            {
                var stopwatch = Stopwatch.StartNew();
                Subject.Handle(new ApplicationShutdownRequested());
                stopwatch.Stop();

                stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
                Subject.MovieCoverQueueTest.WorkerCountAlive.Should().BeGreaterThan(0);
            }
            finally
            {
                releaseCover.Set();
            }

            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.WorkerPoolSize == 0, 5000).Should().BeTrue();
            Subject.MovieCoverQueueTest.WorkerCountAlive.Should().Be(0);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_start_the_movie_cover_worker_pool_only_once()
        {
            var workerCount = MediaCoverService.MovieCoverWorkerCount;

            Subject.Handle(new ApplicationStartedEvent());
            Subject.MovieCoverQueueTest.WorkerPoolSize.Should().Be(workerCount);

            Subject.Handle(new ApplicationShutdownRequested());
            Subject.MovieCoverQueueTest.WorkerPoolSize.Should().Be(0);

            Subject.Handle(new ApplicationStartedEvent());
            Subject.MovieCoverQueueTest.WorkerPoolSize.Should().Be(0);
            Subject.MovieCoverQueueTest.WorkerCountAlive.Should().Be(0);
        }

        [Test]
        public void should_reject_a_delayed_producer_after_shutdown()
        {
            var oldProducerAcquiredSlot = new ManualResetEventSlim();
            var releaseOldProducer = new ManualResetEventSlim();
            var staleMovieProcessed = new ManualResetEventSlim();

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      if (message.Movie.Id == 3970)
                      {
                          staleMovieProcessed.Set();
                      }
                  });

            Subject.MovieCoverQueueTest.SlotAcquired = (movie, _) =>
            {
                if (movie.Id == 3970)
                {
                    oldProducerAcquiredSlot.Set();
                    releaseOldProducer.Wait();
                }
            };

            Subject.Handle(new ApplicationStartedEvent());
            var oldProducer = Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(3970, "https://example.com/stale-lifecycle"))));
            oldProducerAcquiredSlot.Wait(5000).Should().BeTrue();

            Subject.Handle(new ApplicationShutdownRequested());
            releaseOldProducer.Set();
            oldProducer.Wait(5000).Should().BeTrue();

            staleMovieProcessed.Wait(1000).Should().BeFalse();
            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(0);
            Subject.MovieCoverQueueTest.WorkerPoolSize.Should().Be(0);
            Subject.MovieCoverQueueTest.WorkerCountAlive.Should().Be(0);
        }

        [Test]
        public void should_not_start_a_second_worker_pool_while_shutdown_is_in_progress_or_after_timeout()
        {
            var coverStarted = new ManualResetEventSlim();
            var releaseCover = new ManualResetEventSlim();
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            Subject.MovieCoverQueueTest.ShutdownTimeout = TimeSpan.FromMilliseconds(200);

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback(() =>
                  {
                      coverStarted.Set();
                      releaseCover.Wait();
                  })
                  .Returns(true);

            Subject.Handle(new ApplicationStartedEvent());
            Subject.Handle(new MovieUpdatedEvent(GivenMovie(3950)));
            coverStarted.Wait(5000).Should().BeTrue();

            var shutdown = Task.Run(() => Subject.Handle(new ApplicationShutdownRequested()));
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.Stopping, 5000).Should().BeTrue();
            Subject.Handle(new ApplicationStartedEvent());
            shutdown.Wait(5000).Should().BeTrue();
            Subject.Handle(new ApplicationStartedEvent());

            Subject.MovieCoverQueueTest.WorkerPoolSize.Should().Be(workerCount);
            releaseCover.Set();
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.WorkerPoolSize == 0, 5000).Should().BeTrue();
            Subject.MovieCoverQueueTest.WorkerCountAlive.Should().Be(0);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_release_all_blocked_overflow_producers_when_shutdown_starts()
        {
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            var workersStarted = new CountdownEvent(workerCount);
            var releaseWorkers = new ManualResetEventSlim();
            Subject.MovieCoverQueueTest.ShutdownTimeout = TimeSpan.FromMilliseconds(200);

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback(() =>
                  {
                      workersStarted.Signal();
                      releaseWorkers.Wait();
                  })
                  .Returns(true);

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < workerCount; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(7000 + i)));
            }

            workersStarted.Wait(5000).Should().BeTrue();

            for (var i = 0; i < MediaCoverService.MovieCoverQueueCapacity; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(7100 + i)));
            }

            var blocked = new List<Task>
            {
                Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(8000, "https://example.com/old-duplicate"))))
            };
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.BlockedProducerCount == 1, 5000).Should().BeTrue();
            blocked.Add(Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(8001)))));
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.BlockedProducerCount == 2, 5000).Should().BeTrue();
            blocked.Add(Task.Run(() => Subject.Handle(new MovieUpdatedEvent(GivenMovie(8000, "https://example.com/new-duplicate")))));
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.BlockedProducerCount == 3, 5000).Should().BeTrue();

            try
            {
                Subject.Handle(new ApplicationShutdownRequested());
                Task.WaitAll(blocked.ToArray(), 1000).Should().BeTrue();
                Subject.MovieCoverQueueTest.BlockedProducerCount.Should().Be(0);
            }
            finally
            {
                releaseWorkers.Set();
            }

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_abandon_queued_movie_covers_when_shutdown_starts()
        {
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            var workersStarted = new CountdownEvent(workerCount);
            var activeCompleted = new CountdownEvent(workerCount);
            var releaseWorkers = new ManualResetEventSlim();
            var queuedCompleted = new ManualResetEventSlim();
            Subject.MovieCoverQueueTest.ShutdownTimeout = TimeSpan.FromMilliseconds(200);

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((remoteUrl, _) =>
                  {
                      if (remoteUrl.StartsWith("https://example.com/active-"))
                      {
                          workersStarted.Signal();
                          releaseWorkers.Wait();
                      }
                  })
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      if (message.Movie.Id == 4999)
                      {
                          queuedCompleted.Set();
                      }

                      if (message.Movie.Id >= 4000 && message.Movie.Id < 4000 + workerCount)
                      {
                          activeCompleted.Signal();
                      }
                  });

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < workerCount; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(4000 + i, $"https://example.com/active-{i}")));
            }

            workersStarted.Wait(5000).Should().BeTrue();
            Subject.Handle(new MovieUpdatedEvent(GivenMovie(4999, "https://example.com/queued")));

            var stopwatch = Stopwatch.StartNew();
            Subject.Handle(new ApplicationShutdownRequested());
            stopwatch.Stop();

            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
            Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(0);

            releaseWorkers.Set();
            activeCompleted.Wait(5000).Should().BeTrue();
            queuedCompleted.Wait(200).Should().BeFalse();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_continue_processing_after_one_movie_completion_event_fails()
        {
            var blockingWorkers = Math.Max(0, MediaCoverService.MovieCoverWorkerCount - 1);
            var blockersStarted = new CountdownEvent(blockingWorkers);
            var releaseBlockers = new ManualResetEventSlim();
            var secondMovieCompleted = new ManualResetEventSlim();
            var completionCounts = new ConcurrentDictionary<int, int>();
            var handler = (IHandle<MovieUpdatedEvent>)Subject;

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((remoteUrl, _) =>
                  {
                      if (remoteUrl.StartsWith("https://example.com/block-"))
                      {
                          blockersStarted.Signal();
                          releaseBlockers.Wait();
                      }
                  })
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);

            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      completionCounts.AddOrUpdate(message.Movie.Id, 1, (_, count) => count + 1);

                      if (message.Movie.Id == 5000)
                      {
                          throw new InvalidOperationException("synthetic completion failure");
                      }

                      if (message.Movie.Id == 5001)
                      {
                          secondMovieCompleted.Set();
                      }
                  });

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < blockingWorkers; i++)
            {
                handler.Handle(new MovieUpdatedEvent(GivenMovie(6000 + i, $"https://example.com/block-{i}")));
            }

            blockersStarted.Wait(5000).Should().BeTrue();

            try
            {
                handler.Handle(new MovieUpdatedEvent(GivenMovie(5000)));
                handler.Handle(new MovieUpdatedEvent(GivenMovie(5001)));

                secondMovieCompleted.Wait(5000).Should().BeTrue();
                completionCounts.Should().ContainKey(5000).WhoseValue.Should().Be(1);
                completionCounts.Should().ContainKey(5001).WhoseValue.Should().Be(1);
            }
            finally
            {
                releaseBlockers.Set();
            }

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_record_the_source_of_a_downloaded_movie_cover()
        {
            var movie = GivenMovie(7);
            var path = Subject.GetMovieCoverPath(movie.Id, MediaCoverTypes.Poster);

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(RemoteUrl, path))
                  .Returns(false);

            Subject.HandleAsync(new MovieUpdatedEvent(movie));

            Mocker.GetMock<IHttpClient>()
                  .Verify(v => v.DownloadFile(RemoteUrl, path), Times.Once());

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Verify(v => v.RecordSource(RemoteUrl, path), Times.Once());
        }

        [Test]
        public void should_not_download_movie_covers_when_disabled()
        {
            var movie = GivenMovie(7);
            movie.DownloadCovers = false;

            Subject.HandleAsync(new MovieUpdatedEvent(movie));

            Mocker.GetMock<IHttpClient>()
                  .Verify(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_download_scene_covers_when_disabled()
        {
            var movie = GivenMovie(7);
            movie.MovieMetadata.Value.ItemType = ItemType.Scene;
            movie.DownloadCovers = false;

            Subject.HandleAsync(new MovieUpdatedEvent(movie));

            Mocker.GetMock<IHttpClient>()
                  .Verify(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_download_jav_covers_when_disabled()
        {
            var movie = GivenMovie(7);
            movie.MovieMetadata.Value.ItemType = ItemType.Jav;
            movie.DownloadCovers = false;

            Subject.HandleAsync(new MovieUpdatedEvent(movie));

            Mocker.GetMock<IHttpClient>()
                  .Verify(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_download_studio_covers_when_disabled()
        {
            var studio = Builder<Studio>.CreateNew()
                .With(s => s.Id = 9)
                .With(s => s.DownloadCovers = false)
                .With(s => s.Images = new List<MediaCover.MediaCover> { new (MediaCoverTypes.Poster, RemoteUrl) })
                .Build();

            Subject.HandleAsync(new StudioUpdatedEvent(studio));

            Mocker.GetMock<IHttpClient>()
                  .Verify(v => v.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_delete_movie_covers_when_disabled_via_edit()
        {
            var movie = GivenMovie(7);
            movie.DownloadCovers = false;

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.Handle(new MovieEditedEvent(movie, movie));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>()), Times.AtLeastOnce());
            Mocker.GetMock<IHttpClient>()
                  .Verify(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<MovieUpdatedEvent>()), Times.Never());
        }

        [Test]
        public void should_queue_a_movie_cover_check_on_edit_when_enabled()
        {
            var movie = GivenMovie(7);

            Subject.Handle(new MovieEditedEvent(movie, movie));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<MovieUpdatedEvent>(e => e.Movie == movie)), Times.Once());
        }

        [Test]
        public void should_purge_only_disabled_movies_on_bulk_edit()
        {
            var disabled = GivenMovie(7);
            disabled.DownloadCovers = false;
            var enabled = GivenMovie(8);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.Handle(new MoviesBulkEditedEvent(new List<Movie> { disabled, enabled }));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFile(It.Is<string>(p => p.Contains($"{Path.DirectorySeparatorChar}7{Path.DirectorySeparatorChar}"))), Times.AtLeastOnce());
            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<MovieUpdatedEvent>(e => e.Movie == enabled)), Times.Once());
            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<MovieUpdatedEvent>(e => e.Movie == disabled)), Times.Never());
        }

        [Test]
        public void should_download_the_original_tpdb_image_when_the_resized_copy_is_missing()
        {
            const string resizedUrl = "https://cdn.theporndb.net/scene/1/background/c/bg-x-large.webp";
            const string originalUrl = "https://cdn.theporndb.net/scene/1/background/bg-x.webp";

            var movie = GivenMovie(7, resizedUrl);
            var path = Subject.GetMovieCoverPath(movie.Id, MediaCoverTypes.Poster);

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(resizedUrl, path))
                  .Returns(false);

            var request = new HttpRequest(resizedUrl);

            Mocker.GetMock<IHttpClient>()
                  .Setup(v => v.DownloadFile(resizedUrl, It.IsAny<string>()))
                  .Throws(new HttpException(new HttpResponse(request, new HttpHeader(), Array.Empty<byte>(), HttpStatusCode.NotFound)));

            Subject.HandleAsync(new MovieUpdatedEvent(movie));

            Mocker.GetMock<IHttpClient>()
                  .Verify(v => v.DownloadFile(originalUrl, path), Times.Once());

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Verify(v => v.RecordSource(resizedUrl, path), Times.Once());
        }

        [Test]
        public void should_not_record_the_source_when_the_download_fails()
        {
            var movie = GivenMovie(7);

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(false);

            Mocker.GetMock<IHttpClient>()
                  .Setup(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Throws(new WebException("timed out", WebExceptionStatus.Timeout));

            Subject.HandleAsync(new MovieUpdatedEvent(movie));

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Verify(v => v.RecordSource(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_record_the_source_of_a_downloaded_performer_cover()
        {
            var performer = GivenPerformer(8, PerformerVisibility.Visible);
            var path = Subject.GetPerformerCoverPath(performer.Id, MediaCoverTypes.Headshot);

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(RemoteUrl, path))
                  .Returns(false);

            Subject.HandleAsync(new PerformerUpdatedEvent(performer));

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Verify(v => v.RecordSource(RemoteUrl, path), Times.Once());
        }

        [Test]
        public void should_not_download_covers_for_a_hidden_performer()
        {
            var performer = GivenPerformer(8, PerformerVisibility.Hidden);

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(false);

            Subject.HandleAsync(new PerformerUpdatedEvent(performer));

            Mocker.GetMock<IHttpClient>()
                  .Verify(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_the_cover_folder_of_a_hidden_performer()
        {
            var performer = GivenPerformer(8, PerformerVisibility.Hidden);
            var folder = Path.GetDirectoryName(Subject.GetPerformerCoverPath(performer.Id, MediaCoverTypes.Headshot));

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FolderExists(folder))
                  .Returns(true);

            Subject.HandleAsync(new PerformerUpdatedEvent(performer));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFolder(folder, true), Times.Once());
        }

        [Test]
        public void should_not_delete_anything_for_a_hidden_performer_without_covers()
        {
            var performer = GivenPerformer(8, PerformerVisibility.Hidden);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FolderExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.HandleAsync(new PerformerUpdatedEvent(performer));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFolder(It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        [Test]
        public void should_delete_the_cover_folder_of_a_deleted_performer()
        {
            var performer = GivenPerformer(8, PerformerVisibility.Visible);
            var folder = Path.GetDirectoryName(Subject.GetPerformerCoverPath(performer.Id, MediaCoverTypes.Headshot));

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FolderExists(folder))
                  .Returns(true);

            Subject.HandleAsync(new PerformersDeletedEvent(new List<Performer> { performer }));

            Mocker.GetMock<IDiskProvider>()
                  .Verify(v => v.DeleteFolder(folder, true), Times.Once());
        }

        [Test]
        public void should_resize_covers_if_main_downloaded()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(false);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Subject.HandleAsync(new MovieUpdatedEvent(_movie));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_resize_covers_if_missing()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(false);

            Subject.HandleAsync(new MovieUpdatedEvent(_movie));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_not_resize_covers_if_exists()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(1000);

            Subject.HandleAsync(new MovieUpdatedEvent(_movie));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_resize_covers_if_existing_is_empty()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.GetFileSize(It.IsAny<string>()))
                  .Returns(0);

            Subject.HandleAsync(new MovieUpdatedEvent(_movie));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_log_error_if_resize_failed()
        {
            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns(false);

            Mocker.GetMock<IImageResizer>()
                  .Setup(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                  .Throws<ApplicationException>();

            Subject.HandleAsync(new MovieUpdatedEvent(_movie));

            Mocker.GetMock<IImageResizer>()
                  .Verify(v => v.Resize(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public void should_queue_new_movie_covers_without_blocking_when_the_update_queue_is_full()
        {
            var gate = GivenBlockedWorkers();
            GivenAnyNewMovie();

            try
            {
                var fill = Task.Run(() =>
                {
                    for (var i = 0; i < MediaCoverService.MovieCoverQueueCapacity; i++)
                    {
                        Subject.Handle(new MovieUpdatedEvent(GivenMovie(2000 + i)));
                    }
                });

                fill.Wait(5000).Should().BeTrue();
                Subject.MovieCoverQueueTest.PendingUniqueCount.Should().Be(MediaCoverService.MovieCoverQueueCapacity);

                var stopwatch = Stopwatch.StartNew();
                Subject.QueueNewMovieCovers(Enumerable.Range(10_000, 1000));
                stopwatch.Stop();

                stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
                Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(1000);
            }
            finally
            {
                ReleaseBlockedWorkers(gate);
            }
        }

        [Test]
        public void should_download_new_movie_covers_without_checking_the_disk()
        {
            var completed = GivenCompletionSignal(3);
            var path = Subject.GetMovieCoverPath(3, MediaCoverTypes.Poster);
            GivenNewMovie(3);

            Subject.Handle(new ApplicationStartedEvent());
            Subject.QueueNewMovieCovers(new[] { 3 });

            completed.Wait(5000).Should().BeTrue();

            Mocker.GetMock<IHttpClient>().Verify(v => v.DownloadFile(RemoteUrl, path), Times.Once());
            Mocker.GetMock<ICoverExistsSpecification>().Verify(v => v.RecordSource(RemoteUrl, path), Times.Once());
            Mocker.GetMock<ICoverExistsSpecification>().Verify(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.FileExists(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_process_movie_updates_before_new_movie_covers()
        {
            const string newUrl = "https://example.com/download-new";
            const string updateUrl = "https://example.com/download-update";
            var gate = GivenBlockedWorkers();
            var order = new ConcurrentQueue<string>();
            var downloaded = new CountdownEvent(2);

            Mocker.GetMock<IHttpClient>()
                  .Setup(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string>((remoteUrl, _) =>
                  {
                      order.Enqueue(remoteUrl);
                      downloaded.Signal();
                  });
            GivenNewMovie(20, newUrl);

            try
            {
                Subject.QueueNewMovieCovers(new[] { 20 });
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(21, updateUrl)));

                // One free worker drains both queues in order, so nothing races it.
                gate.Release(1);

                downloaded.Wait(5000).Should().BeTrue();
                order.ToArray().Should().Equal(updateUrl, newUrl);
            }
            finally
            {
                ReleaseBlockedWorkers(gate);
            }
        }

        [Test]
        public void should_download_a_duplicated_new_movie_once()
        {
            var gate = GivenBlockedWorkers();
            var completed = GivenCompletionSignal(5);
            GivenNewMovie(5);

            try
            {
                Subject.QueueNewMovieCovers(new[] { 5, 5 });
                Subject.QueueNewMovieCovers(new[] { 5 });

                Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(1);
            }
            finally
            {
                ReleaseBlockedWorkers(gate);
            }

            completed.Wait(5000).Should().BeTrue();
            Mocker.GetMock<IMovieService>().Verify(v => v.GetMovie(5), Times.Once());
        }

        [Test]
        public void should_skip_a_new_movie_already_queued_by_an_update()
        {
            // One worker stays mid-download, so another has to drain the backlog.
            Assume.That(MediaCoverService.MovieCoverWorkerCount, Is.GreaterThanOrEqualTo(2));

            const string updateUrl = "https://example.com/download-7";
            var gate = GivenBlockedWorkers();
            var downloadStarted = new ManualResetEventSlim();
            var releaseDownload = new ManualResetEventSlim();
            var completed = GivenCompletionSignal(7);

            Mocker.GetMock<IHttpClient>()
                  .Setup(v => v.DownloadFile(updateUrl, It.IsAny<string>()))
                  .Callback(() =>
                  {
                      downloadStarted.Set();
                      releaseDownload.Wait();
                  });
            GivenNewMovie(7, updateUrl);

            try
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(7, updateUrl)));
                Subject.QueueNewMovieCovers(new[] { 7 });
                ReleaseBlockedWorkers(gate);

                // The update is mid-download, so the backlog entry has to be dropped, not run too.
                downloadStarted.Wait(5000).Should().BeTrue();
                SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount == 0, 5000).Should().BeTrue();
            }
            finally
            {
                releaseDownload.Set();
            }

            completed.Wait(5000).Should().BeTrue();
            Mocker.GetMock<IHttpClient>().Verify(v => v.DownloadFile(updateUrl, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(v => v.GetMovie(7), Times.Never());
        }

        [Test]
        public void should_skip_a_new_movie_that_no_longer_exists()
        {
            var completed = GivenCompletionSignal(10);

            Mocker.GetMock<IMovieService>()
                  .Setup(v => v.GetMovie(9))
                  .Throws(new ModelNotFoundException(typeof(Movie), 9));
            GivenNewMovie(10);

            Subject.Handle(new ApplicationStartedEvent());
            Subject.QueueNewMovieCovers(new[] { 9, 10 });

            completed.Wait(5000).Should().BeTrue();
            Mocker.GetMock<IHttpClient>().Verify(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IHttpClient>().Verify(v => v.DownloadFile(It.IsAny<string>(), Subject.GetMovieCoverPath(10, MediaCoverTypes.Poster)), Times.Once());
        }

        [Test]
        public void should_drop_new_movie_covers_for_deleted_movies()
        {
            var gate = GivenBlockedWorkers();
            var completed = GivenCompletionSignal(12);
            GivenNewMovie(11);
            GivenNewMovie(12);

            try
            {
                Subject.QueueNewMovieCovers(new[] { 11, 12 });
                Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { GivenMovie(11) }, false, false));

                Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(1);
            }
            finally
            {
                ReleaseBlockedWorkers(gate);
            }

            completed.Wait(5000).Should().BeTrue();
            Mocker.GetMock<IMovieService>().Verify(v => v.GetMovie(11), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(v => v.GetMovie(12), Times.Once());
        }

        [Test]
        public void should_stop_within_the_shutdown_deadline_with_a_large_new_movie_backlog()
        {
            var getMovieCalls = 0;
            var gate = GivenBlockedWorkers();
            Subject.MovieCoverQueueTest.ShutdownTimeout = TimeSpan.FromSeconds(2);

            Mocker.GetMock<IMovieService>()
                  .Setup(v => v.GetMovie(It.IsAny<int>()))
                  .Returns<int>(id =>
                  {
                      Interlocked.Increment(ref getMovieCalls);
                      return GivenMovie(id);
                  });

            Subject.QueueNewMovieCovers(Enumerable.Range(30_000, 5000));
            Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(5000);

            // Shutdown joins the workers, so it has to run beside the release.
            var shutdown = Task.Run(() => Subject.Handle(new ApplicationShutdownRequested()));
            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.Stopping, 5000).Should().BeTrue();
            ReleaseBlockedWorkers(gate);

            SpinWait.SpinUntil(() => Subject.MovieCoverQueueTest.WorkerCountAlive == 0, 3000).Should().BeTrue();
            shutdown.Wait(5000).Should().BeTrue();
            Volatile.Read(ref getMovieCalls).Should().BeLessThan(100);
        }

        [Test]
        public void should_remove_covers_of_a_new_movie_deleted_while_downloading()
        {
            const int deletedMovieId = 12450;
            var downloadStarted = new ManualResetEventSlim();
            var releaseDownload = new ManualResetEventSlim();
            var coverCompleted = GivenCompletionSignal(deletedMovieId);
            var folderPresent = 1;
            var deleteCalls = 0;
            var deletedMovie = GivenMovie(deletedMovieId, "https://example.com/deleted-new");

            Mocker.GetMock<IMovieService>().Setup(v => v.GetMovie(deletedMovieId)).Returns(deletedMovie);
            Mocker.GetMock<IHttpClient>()
                  .Setup(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback(() =>
                  {
                      downloadStarted.Set();
                      releaseDownload.Wait();
                      Volatile.Write(ref folderPresent, 1);
                  });
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FolderExists(It.IsAny<string>()))
                  .Returns(() => Volatile.Read(ref folderPresent) == 1);
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.DeleteFolder(It.IsAny<string>(), true))
                  .Callback(() =>
                  {
                      Volatile.Write(ref folderPresent, 0);
                      Interlocked.Increment(ref deleteCalls);
                  });

            Subject.Handle(new ApplicationStartedEvent());
            Subject.QueueNewMovieCovers(new[] { deletedMovieId });
            downloadStarted.Wait(5000).Should().BeTrue();

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { deletedMovie }, false, false));
            Volatile.Read(ref folderPresent).Should().Be(0);
            releaseDownload.Set();

            coverCompleted.Wait(5000).Should().BeTrue();
            SpinWait.SpinUntil(() => Volatile.Read(ref deleteCalls) >= 2, 5000).Should().BeTrue();
            Volatile.Read(ref folderPresent).Should().Be(0);

            var folder = Path.GetDirectoryName(Subject.GetMovieCoverPath(deletedMovieId, MediaCoverTypes.Poster));
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFolder(folder, true), Times.AtLeast(2));
        }

        [Test]
        public void should_ignore_new_movie_covers_before_start_and_after_shutdown()
        {
            Subject.QueueNewMovieCovers(new[] { 1 });
            Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(0);

            Subject.Handle(new ApplicationStartedEvent());
            Subject.Handle(new ApplicationShutdownRequested());

            Subject.QueueNewMovieCovers(new[] { 1 });

            Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(0);
            Mocker.GetMock<IMovieService>().Verify(v => v.GetMovie(1), Times.Never());
        }

        [Test]
        public void should_ignore_an_empty_new_movie_list()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Subject.QueueNewMovieCovers(Array.Empty<int>());

            Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(0);
        }

        [Test]
        public void should_not_download_for_a_new_movie_without_images()
        {
            bool? updated = null;
            var completed = new ManualResetEventSlim();
            var movie = GivenMovie(40);
            movie.MovieMetadata.Value.Images = new List<MediaCover.MediaCover>();

            Mocker.GetMock<IMovieService>().Setup(v => v.GetMovie(40)).Returns(movie);
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      updated = message.Updated;
                      completed.Set();
                  });

            Subject.Handle(new ApplicationStartedEvent());
            Subject.QueueNewMovieCovers(new[] { 40 });

            completed.Wait(5000).Should().BeTrue();
            updated.Should().BeFalse();
            Mocker.GetMock<IHttpClient>().Verify(v => v.DownloadFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_hold_new_movie_covers_while_the_migration_is_running()
        {
            var processed = new CountdownEvent(2);
            var paused = true;

            Mocker.GetMock<ILegacyMigrationGate>().SetupGet(v => v.AutomationPaused).Returns(() => Volatile.Read(ref paused));
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(_ => processed.Signal());
            GivenAnyNewMovie();
            Subject.Handle(new ApplicationStartedEvent());

            Subject.QueueNewMovieCovers(new[] { 21, 22 });
            Thread.Sleep(500);

            Mocker.GetMock<IMovieService>().Verify(v => v.GetMovie(It.IsAny<int>()), Times.Never());
            Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(2);

            Volatile.Write(ref paused, false);
            Subject.Handle(new LegacyMigrationCompletedEvent());

            processed.Wait(5000).Should().BeTrue();
        }

        [Test]
        public void should_process_movie_updates_while_the_migration_is_running()
        {
            Mocker.GetMock<ILegacyMigrationGate>().SetupGet(v => v.AutomationPaused).Returns(true);
            var completed = GivenCompletionSignal(40);
            Subject.Handle(new ApplicationStartedEvent());

            Subject.Handle(new MovieUpdatedEvent(GivenMovie(40)));

            completed.Wait(5000).Should().BeTrue();
        }

        [Test]
        public void should_resume_new_movie_covers_queued_before_the_migration_started()
        {
            var processed = new CountdownEvent(3);
            var paused = false;
            var gate = GivenBlockedWorkers();

            Mocker.GetMock<ILegacyMigrationGate>().SetupGet(v => v.AutomationPaused).Returns(() => Volatile.Read(ref paused));
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      if (message.Movie.Id < 1000)
                      {
                          processed.Signal();
                      }
                  });
            GivenAnyNewMovie();

            try
            {
                Subject.QueueNewMovieCovers(new[] { 31, 32, 33 });
                Volatile.Write(ref paused, true);
                ReleaseBlockedWorkers(gate);
                Thread.Sleep(500);

                processed.CurrentCount.Should().Be(3);
                Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(3);

                Volatile.Write(ref paused, false);
                Subject.Handle(new LegacyMigrationCompletedEvent());

                processed.Wait(5000).Should().BeTrue();
            }
            finally
            {
                ReleaseBlockedWorkers(gate);
            }
        }

        [Test]
        public void should_keep_the_worker_alive_when_the_migration_gate_read_fails()
        {
            var processed = new CountdownEvent(1);

            Mocker.GetMock<ILegacyMigrationGate>()
                  .SetupGet(v => v.AutomationPaused)
                  .Throws(new InvalidOperationException("database is locked"));
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(_ => processed.Signal());
            GivenAnyNewMovie();
            Subject.Handle(new ApplicationStartedEvent());

            Subject.QueueNewMovieCovers(new[] { 51 });

            processed.Wait(5000).Should().BeTrue();

            var updateCompleted = GivenCompletionSignal(52);
            Subject.Handle(new MovieUpdatedEvent(GivenMovie(52)));

            updateCompleted.Wait(5000).Should().BeTrue();
            Subject.MovieCoverQueueTest.WorkerCountAlive.Should().Be(MediaCoverService.MovieCoverWorkerCount);

            // One per worker wake-up: the new movie and the update.
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void should_not_read_the_migration_gate_when_queueing_new_movie_covers()
        {
            var gate = GivenBlockedWorkers();
            var reads = 0;

            Mocker.GetMock<ILegacyMigrationGate>()
                  .SetupGet(v => v.AutomationPaused)
                  .Returns(() =>
                  {
                      Interlocked.Increment(ref reads);
                      return false;
                  });

            try
            {
                Subject.QueueNewMovieCovers(new[] { 1, 2, 3 });

                Volatile.Read(ref reads).Should().Be(0);
                Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(3);
            }
            finally
            {
                ReleaseBlockedWorkers(gate);
            }
        }

        [Test]
        public void should_not_block_producers_while_a_worker_reads_the_migration_gate()
        {
            var readStarted = new ManualResetEventSlim();
            var releaseRead = new ManualResetEventSlim();

            Mocker.GetMock<ILegacyMigrationGate>()
                  .SetupGet(v => v.AutomationPaused)
                  .Returns(() =>
                  {
                      readStarted.Set();
                      releaseRead.Wait(5000);
                      return true;
                  });
            Subject.Handle(new ApplicationStartedEvent());

            try
            {
                Subject.QueueNewMovieCovers(new[] { 61 });
                readStarted.Wait(5000).Should().BeTrue();

                // A worker is inside the gate read; producers take the same lock and must not wait for it.
                var produce = Task.Run(() =>
                {
                    Subject.QueueNewMovieCovers(new[] { 62 });
                    Subject.Handle(new MovieUpdatedEvent(GivenMovie(63)));
                });

                produce.Wait(1000).Should().BeTrue();
            }
            finally
            {
                releaseRead.Set();
            }
        }

        [Test]
        public void should_fetch_covers_held_across_a_restart_on_completion()
        {
            var startedAt = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            var processed = new CountdownEvent(3);
            var ids = new ConcurrentBag<int>();
            var paused = true;

            Mocker.GetMock<ILegacyMigrationGate>().SetupGet(v => v.AutomationPaused).Returns(() => Volatile.Read(ref paused));
            Mocker.GetMock<IMovieService>().Setup(v => v.AllMovieIdsAddedSince(startedAt)).Returns(new List<int> { 71, 72, 73 });
            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      ids.Add(message.Movie.Id);
                      processed.Signal();
                  });
            GivenAnyNewMovie();
            Subject.Handle(new ApplicationStartedEvent());

            // 72 was queued after the restart; 71 and 73 only exist in the database.
            Subject.QueueNewMovieCovers(new[] { 72 });
            Thread.Sleep(200);

            Volatile.Write(ref paused, false);
            Subject.Handle(new LegacyMigrationCompletedEvent { StartedAt = startedAt });

            processed.Wait(5000).Should().BeTrue();
            ids.Should().BeEquivalentTo(new[] { 71, 72, 73 });
            Mocker.GetMock<IMovieService>().Verify(v => v.GetMovie(72), Times.Once());
        }

        [Test]
        public void should_not_query_added_movies_on_completion_without_a_start_time()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Subject.Handle(new LegacyMigrationCompletedEvent());

            Mocker.GetMock<IMovieService>().Verify(v => v.AllMovieIdsAddedSince(It.IsAny<DateTime>()), Times.Never());
        }

        [Test]
        public void should_release_held_covers_when_the_added_movie_read_fails()
        {
            var processed = GivenCompletionSignal(81);
            var paused = true;

            Mocker.GetMock<ILegacyMigrationGate>().SetupGet(v => v.AutomationPaused).Returns(() => Volatile.Read(ref paused));
            Mocker.GetMock<IMovieService>()
                  .Setup(v => v.AllMovieIdsAddedSince(It.IsAny<DateTime>()))
                  .Throws(new InvalidOperationException("database is locked"));
            GivenAnyNewMovie();
            Subject.Handle(new ApplicationStartedEvent());

            Subject.QueueNewMovieCovers(new[] { 81 });
            Thread.Sleep(200);

            Volatile.Write(ref paused, false);
            Subject.Handle(new LegacyMigrationCompletedEvent { StartedAt = DateTime.UtcNow });

            processed.Wait(5000).Should().BeTrue();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_download_a_new_movie_deleted_and_added_again_once()
        {
            var gate = GivenBlockedWorkers();
            var completed = GivenCompletionSignal(13);
            GivenNewMovie(13);

            try
            {
                Subject.QueueNewMovieCovers(new[] { 13 });
                Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { GivenMovie(13) }, false, false));
                Subject.QueueNewMovieCovers(new[] { 13 });

                Subject.MovieCoverQueueTest.NewMovieCoverBacklogCount.Should().Be(1);
            }
            finally
            {
                ReleaseBlockedWorkers(gate);
            }

            completed.Wait(5000).Should().BeTrue();
            Thread.Sleep(200);
            Mocker.GetMock<IMovieService>().Verify(v => v.GetMovie(13), Times.Once());
        }

        // Starts the worker pool and parks every worker in AlreadyExists on a movie update. Updates
        // whose URL contains "/download-" report a missing cover so they reach DownloadFile.
        private SemaphoreSlim GivenBlockedWorkers()
        {
            var workerCount = MediaCoverService.MovieCoverWorkerCount;
            var started = new CountdownEvent(workerCount);
            var gate = new SemaphoreSlim(0);

            Mocker.GetMock<ICoverExistsSpecification>()
                  .Setup(v => v.AlreadyExists(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns<string, string>((remoteUrl, _) =>
                  {
                      if (remoteUrl.StartsWith("https://example.com/block-"))
                      {
                          started.Signal();
                          gate.Wait();
                      }

                      return !remoteUrl.Contains("/download-");
                  });
            Mocker.GetMock<IDiskProvider>().Setup(v => v.FileExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(v => v.GetFileSize(It.IsAny<string>())).Returns(1000);

            Subject.Handle(new ApplicationStartedEvent());

            for (var i = 0; i < workerCount; i++)
            {
                Subject.Handle(new MovieUpdatedEvent(GivenMovie(1000 + i, $"https://example.com/block-{i}")));
            }

            started.Wait(5000).Should().BeTrue();

            return gate;
        }

        private static void ReleaseBlockedWorkers(SemaphoreSlim gate)
        {
            gate.Release(MediaCoverService.MovieCoverWorkerCount);
        }

        private void GivenNewMovie(int id, string remoteUrl = RemoteUrl)
        {
            var movie = GivenMovie(id, remoteUrl);

            Mocker.GetMock<IMovieService>().Setup(v => v.GetMovie(id)).Returns(movie);
        }

        private void GivenAnyNewMovie()
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(v => v.GetMovie(It.IsAny<int>()))
                  .Returns<int>(id => GivenMovie(id));
        }

        private ManualResetEventSlim GivenCompletionSignal(int movieId)
        {
            var completed = new ManualResetEventSlim();

            Mocker.GetMock<IEventAggregator>()
                  .Setup(v => v.PublishEvent(It.IsAny<MediaCoversUpdatedEvent>()))
                  .Callback<MediaCoversUpdatedEvent>(message =>
                  {
                      if (message.Movie.Id == movieId)
                      {
                          completed.Set();
                      }
                  });

            return completed;
        }

        private static Movie GivenMovie(int id, string remoteUrl = RemoteUrl)
        {
            return Builder<Movie>.CreateNew()
                .With(v => v.Id = id)
                .With(v => v.MovieMetadata.Value.Images = new List<MediaCover.MediaCover>
                {
                    new (MediaCoverTypes.Poster, remoteUrl)
                })
                .Build();
        }

        private static List<MediaCover.MediaCover> GivenCovers(string remoteUrl = RemoteUrl)
        {
            return new List<MediaCover.MediaCover>
            {
                new () { CoverType = MediaCoverTypes.Banner, RemoteUrl = remoteUrl }
            };
        }

        private static Performer GivenPerformer(int id, PerformerVisibility visibility)
        {
            return Builder<Performer>.CreateNew()
                .With(p => p.Id = id)
                .With(p => p.Visibility = visibility)
                .With(p => p.Images = new List<MediaCover.MediaCover> { new(MediaCoverTypes.Headshot, RemoteUrl) })
                .Build();
        }
    }
}
