using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class AssignDefaultQualityProfilesFixture : DbTest<AssignDefaultQualityProfiles, Movie>
    {
        private Mock<ICached> _v3Cache;
        private Mock<ICached> _v5Cache;

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IQualityProfileRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<QualityProfile> { new() { Id = 1 } });

            _v3Cache = new Mock<ICached>();
            _v5Cache = new Mock<ICached>();

            Mocker.GetMock<ICacheManager>().Setup(s => s.FindCache(ApiResourceCaches.Movie[0])).Returns(_v3Cache.Object);
            Mocker.GetMock<ICacheManager>().Setup(s => s.FindCache(ApiResourceCaches.Movie[1])).Returns(_v5Cache.Object);
        }

        [Test]
        public void should_revoke_an_impacted_movie_from_both_api_versions_caches()
        {
            var movie = Builder<Movie>.CreateNew()
                                      .With(m => m.QualityProfileId = 99)
                                      .BuildNew();

            Db.Insert(movie);

            Subject.Clean();

            Db.All<Movie>().Should().ContainSingle().Which.QualityProfileId.Should().Be(1);
            _v3Cache.Verify(c => c.Remove(movie.Id.ToString()), Times.Once());
            _v5Cache.Verify(c => c.Remove(movie.Id.ToString()), Times.Once());
        }

        [Test]
        public void should_not_revoke_a_movie_with_a_valid_profile()
        {
            var movie = Builder<Movie>.CreateNew()
                                      .With(m => m.QualityProfileId = 1)
                                      .BuildNew();

            Db.Insert(movie);

            Subject.Clean();

            _v3Cache.Verify(c => c.Remove(It.IsAny<string>()), Times.Never());
            _v5Cache.Verify(c => c.Remove(It.IsAny<string>()), Times.Never());
        }
    }
}
