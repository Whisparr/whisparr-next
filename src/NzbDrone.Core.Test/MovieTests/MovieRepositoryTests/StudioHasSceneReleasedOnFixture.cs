using System;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieRepositoryTests
{
    [TestFixture]
    public class StudioHasSceneReleasedOnFixture : DbTest<MovieRepository, Movie>
    {
        private const string Studio = "studio-1";

        private void GivenScene(string studioForeignId, DateTime? releaseDateUtc, string foreignId)
        {
            var metadata = Db.Insert(Builder<MovieMetadata>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.ForeignId = foreignId)
                .With(m => m.StudioForeignId = studioForeignId)
                .With(m => m.ReleaseDateUtc = releaseDateUtc)
                .BuildNew());

            Subject.Insert(Builder<Movie>.CreateNew()
                .With(m => m.Id = 0)
                .With(m => m.MovieMetadataId = metadata.Id)
                .BuildNew());
        }

        [Test]
        public void should_find_scene_released_that_day()
        {
            GivenScene(Studio, new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc), "a");

            Subject.StudioHasSceneReleasedOn(Studio, new DateTime(2026, 11, 14)).Should().BeTrue();
        }

        [TestCase(13)]
        [TestCase(15)]
        public void should_not_find_scene_released_on_a_neighbouring_day(int releaseDay)
        {
            GivenScene(Studio, new DateTime(2026, 11, releaseDay, 0, 0, 0, DateTimeKind.Utc), "a");

            Subject.StudioHasSceneReleasedOn(Studio, new DateTime(2026, 11, 14)).Should().BeFalse();
        }

        [Test]
        public void should_not_find_another_studios_scene_or_one_without_a_date()
        {
            GivenScene("studio-2", new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc), "a");
            GivenScene(Studio, null, "b");

            Subject.StudioHasSceneReleasedOn(Studio, new DateTime(2026, 11, 14)).Should().BeFalse();
        }
    }
}
