using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class MetadataSourceServiceFixture : CoreTest<MetadataSourceService>
    {
        private void GivenConfig(SceneMetadataType scene, MovieMetadataType movie, bool locked)
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrSceneMetadataSource).Returns(scene);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.WhisparrMovieMetadataSource).Returns(movie);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.MetadataSourcesLocked).Returns(locked);
        }

        [Test]
        public void should_report_current_values()
        {
            GivenConfig(SceneMetadataType.Tpdb, MovieMetadataType.TPDB, true);

            var sources = Subject.Get();

            sources.Scene.Should().Be(SceneMetadataType.Tpdb);
            sources.Movie.Should().Be(MovieMetadataType.TPDB);
            sources.Locked.Should().BeTrue();
        }

        [Test]
        public void should_choose_and_lock_sources()
        {
            GivenConfig(SceneMetadataType.StashDb, MovieMetadataType.TMDB, false);

            Subject.Choose(SceneMetadataType.Tpdb, MovieMetadataType.TPDB);

            Mocker.GetMock<IConfigService>()
                  .Verify(v => v.LockMetadataSources(SceneMetadataType.Tpdb, MovieMetadataType.TPDB), Times.Once());
        }

        [Test]
        public void should_throw_when_choosing_after_lock()
        {
            GivenConfig(SceneMetadataType.StashDb, MovieMetadataType.TMDB, true);

            Action action = () => Subject.Choose(SceneMetadataType.Tpdb, MovieMetadataType.TPDB);

            action.Should().Throw<MetadataSourcesLockedException>();

            Mocker.GetMock<IConfigService>()
                  .Verify(v => v.LockMetadataSources(It.IsAny<SceneMetadataType>(), It.IsAny<MovieMetadataType>()), Times.Never());
        }

        [Test]
        public void should_reject_none_movie_source()
        {
            GivenConfig(SceneMetadataType.StashDb, MovieMetadataType.TMDB, false);

            Action action = () => Subject.Choose(SceneMetadataType.StashDb, MovieMetadataType.NONE);

            action.Should().Throw<ArgumentException>();

            Mocker.GetMock<IConfigService>()
                  .Verify(v => v.LockMetadataSources(It.IsAny<SceneMetadataType>(), It.IsAny<MovieMetadataType>()), Times.Never());
        }

        [Test]
        public void should_lock_current_values_on_first_add()
        {
            GivenConfig(SceneMetadataType.StashDb, MovieMetadataType.TMDB, false);

            Subject.EnsureLocked();

            Mocker.GetMock<IConfigService>()
                  .Verify(v => v.LockMetadataSources(SceneMetadataType.StashDb, MovieMetadataType.TMDB), Times.Once());
        }

        [Test]
        public void should_not_relock_when_already_locked()
        {
            GivenConfig(SceneMetadataType.Tpdb, MovieMetadataType.TPDB, true);

            Subject.EnsureLocked();

            Mocker.GetMock<IConfigService>()
                  .Verify(v => v.LockMetadataSources(It.IsAny<SceneMetadataType>(), It.IsAny<MovieMetadataType>()), Times.Never());
        }
    }
}
