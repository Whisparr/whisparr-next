using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.MovieStats;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Studios;
using Whisparr.Http.REST;

namespace NzbDrone.Api.Test.v5.Studios
{
    [TestFixture]
    public class StudioControllerJavFixture : TestBase<StudioController>
    {
        private const string JavMessage = "JavRequiresTpdbSceneSource";

        private void GivenJavAvailable(bool available)
        {
            Mocker.GetMock<IJavAvailability>().Setup(s => s.IsAvailable()).Returns(available);
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(JavMessage))
                  .Returns(JavMessage);
        }

        private List<string> SharedErrors(StudioResource resource)
        {
            var validator = (ResourceValidator<StudioResource>)typeof(RestController<StudioResource>)
                .GetProperty("SharedValidator", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Subject);

            return validator.Validate(resource).Errors.Select(e => e.ErrorMessage).ToList();
        }

        private static StudioResource Resource(bool javMonitored)
        {
            return new StudioResource { Id = 1, ForeignId = "s", Title = "S", SearchTitle = "s", JavMonitored = javMonitored };
        }

        [Test]
        public void studio_put_jav_monitored_rejected_under_stashdb()
        {
            GivenJavAvailable(false);

            SharedErrors(Resource(true)).Should().Contain(JavMessage);
        }

        [Test]
        public void studio_put_jav_monitored_accepted_when_tpdb_available()
        {
            GivenJavAvailable(true);

            SharedErrors(Resource(true)).Should().NotContain(JavMessage);
        }

        [Test]
        public void studio_put_jav_unmonitored_is_accepted_under_stashdb()
        {
            GivenJavAvailable(false);

            SharedErrors(Resource(false)).Should().NotContain(JavMessage);
        }

        [Test]
        public void studio_resource_maps_jav_fields()
        {
            var studio = new Studio
            {
                Id = 1,
                ForeignId = "s",
                JavMonitored = true,
                HasJav = true,
                JavCount = 2,
                TotalJavCount = 5
            };

            var resource = studio.ToResource();

            resource.JavMonitored.Should().BeTrue();
            resource.HasJav.Should().BeTrue();
            resource.JavCount.Should().Be(2);
            resource.TotalJavCount.Should().Be(5);

            using var json = JsonDocument.Parse(STJson.ToJson(resource));
            json.RootElement.GetProperty("javMonitored").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("hasJav").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("javCount").GetInt32().Should().Be(2);
            json.RootElement.GetProperty("totalJavCount").GetInt32().Should().Be(5);

            Resource(true).ToModel().JavMonitored.Should().BeTrue();
        }

        [Test]
        public void studio_put_with_jav_monitored_persists_true()
        {
            GivenJavAvailable(true);

            var existing = new Studio { Id = 1, ForeignId = "s", Title = "S", SearchTitle = "s" };
            Studio saved = null;

            Mocker.GetMock<IStudioService>().Setup(s => s.GetById(1)).Returns(existing);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetByStudioForeignId("s")).Returns(new List<Movie>());
            Mocker.GetMock<IMovieStatisticsService>()
                  .Setup(s => s.MovieStatistics(It.IsAny<List<int>>()))
                  .Returns(new List<MovieStatistics>());
            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.Update(It.IsAny<Studio>()))
                  .Callback<Studio>(s => saved = s)
                  .Returns<Studio>(s => s);

            Subject.Update(Resource(true));

            saved.JavMonitored.Should().BeTrue();
        }
    }
}
