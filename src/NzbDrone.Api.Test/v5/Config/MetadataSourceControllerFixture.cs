using System.Net;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Config;
using Whisparr.Http.REST;
using NzbDroneClientException = NzbDrone.Core.Exceptions.NzbDroneClientException;

namespace NzbDrone.Api.Test.v5.Config
{
    [TestFixture]
    public class MetadataSourceControllerFixture : TestBase<MetadataSourceController>
    {
        private void GivenSources(SceneMetadataType scene, MovieMetadataType movie, bool locked)
        {
            Mocker.GetMock<IMetadataSourceService>()
                  .Setup(s => s.Get())
                  .Returns(new MetadataSources { Scene = scene, Movie = movie, Locked = locked });
        }

        [Test]
        public void should_get_sources()
        {
            GivenSources(SceneMetadataType.Tpdb, MovieMetadataType.TPDB, true);

            var resource = Subject.GetMetadataSources();

            resource.SceneMetadataSource.Should().Be(SceneMetadataType.Tpdb);
            resource.MovieMetadataSource.Should().Be(MovieMetadataType.TPDB);
            resource.Locked.Should().BeTrue();
        }

        [TestCase(SceneMetadataType.StashDb, MovieMetadataType.TMDB, false, "stashDb", "tmdb")]
        [TestCase(SceneMetadataType.Tpdb, MovieMetadataType.TPDB, true, "tpdb", "tpdb")]
        [TestCase(SceneMetadataType.StashDb, MovieMetadataType.NONE, false, "stashDb", "none")]
        public void should_serialize_sources_as_camel_case_strings(SceneMetadataType scene, MovieMetadataType movie, bool locked, string sceneJson, string movieJson)
        {
            var resource = new MetadataSourceResource { SceneMetadataSource = scene, MovieMetadataSource = movie, Locked = locked };

            using var json = JsonDocument.Parse(STJson.ToJson(resource));

            json.RootElement.GetProperty("sceneMetadataSource").GetString().Should().Be(sceneJson);
            json.RootElement.GetProperty("movieMetadataSource").GetString().Should().Be(movieJson);
            json.RootElement.GetProperty("locked").GetBoolean().Should().Be(locked);
        }

        [Test]
        public void should_deserialize_a_put_body()
        {
            var resource = STJson.Deserialize<MetadataSourceResource>("{ \"sceneMetadataSource\": \"tpdb\", \"movieMetadataSource\": \"tpdb\" }");

            resource.SceneMetadataSource.Should().Be(SceneMetadataType.Tpdb);
            resource.MovieMetadataSource.Should().Be(MovieMetadataType.TPDB);
        }

        [Test]
        public void should_choose_sources_when_unlocked()
        {
            GivenSources(SceneMetadataType.Tpdb, MovieMetadataType.TPDB, true);

            var result = Subject.ChooseMetadataSources(new MetadataSourceResource
            {
                SceneMetadataSource = SceneMetadataType.Tpdb,
                MovieMetadataSource = MovieMetadataType.TPDB
            });

            Mocker.GetMock<IMetadataSourceService>()
                  .Verify(s => s.Choose(SceneMetadataType.Tpdb, MovieMetadataType.TPDB), Times.Once());

            result.Locked.Should().BeTrue();
            result.SceneMetadataSource.Should().Be(SceneMetadataType.Tpdb);
        }

        [Test]
        public void should_answer_409_when_locked()
        {
            Mocker.GetMock<IMetadataSourceService>()
                  .Setup(s => s.Choose(It.IsAny<SceneMetadataType>(), It.IsAny<MovieMetadataType>()))
                  .Throws(new MetadataSourcesLockedException());

            var ex = Assert.Throws<NzbDroneClientException>(() => Subject.ChooseMetadataSources(new MetadataSourceResource
            {
                SceneMetadataSource = SceneMetadataType.StashDb,
                MovieMetadataSource = MovieMetadataType.TMDB
            }));

            ex.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        [Test]
        public void should_answer_400_for_none_movie_source()
        {
            var ex = Assert.Throws<BadRequestException>(() => Subject.ChooseMetadataSources(new MetadataSourceResource
            {
                SceneMetadataSource = SceneMetadataType.StashDb,
                MovieMetadataSource = MovieMetadataType.NONE
            }));

            ex.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            Mocker.GetMock<IMetadataSourceService>()
                  .Verify(s => s.Choose(It.IsAny<SceneMetadataType>(), It.IsAny<MovieMetadataType>()), Times.Never());
        }

        [Test]
        public void should_answer_400_for_an_empty_body()
        {
            Assert.Throws<BadRequestException>(() => Subject.ChooseMetadataSources(null));
        }
    }
}
