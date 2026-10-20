using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Discord;
using NzbDrone.Core.Notifications.Discord.Payloads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.DiscordTests
{
    [TestFixture]
    public class DiscordFixture : CoreTest<Discord>
    {
        private const string SceneForeignId = "6c1d4e2a-9b8f-4c3d-a2e1-7f0b5d9c8e41";

        private DiscordPayload _payload;

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new NotificationDefinition { Settings = new DiscordSettings() };

            Mocker.GetMock<IDiscordProxy>()
                .Setup(s => s.SendPayload(It.IsAny<DiscordPayload>(), It.IsAny<DiscordSettings>()))
                .Callback<DiscordPayload, DiscordSettings>((payload, _) => _payload = payload);
        }

        // Custom format strings render the year in the current culture's calendar, so a host set
        // to a non-Gregorian locale sent Discord a year like 2569 (Buddhist) or 1448 (Hijri) and
        // Discord rejected or misrendered the embed. "O" is culture-invariant.
        [TestCase("en-US")]
        [TestCase("th-TH")]
        [TestCase("ar-SA")]
        public void should_send_a_gregorian_timestamp_regardless_of_culture(string culture)
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);

                Subject.OnApplicationUpdate(new ApplicationUpdateMessage
                {
                    Message = "Updated",
                    PreviousVersion = new Version(3, 4, 0),
                    NewVersion = new Version(3, 5, 0)
                });

                var timestamp = _payload.Embeds[0].Timestamp;

                DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                    .Should().BeTrue();

                parsed.Year.Should().Be(DateTime.UtcNow.Year);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        private static Movie GivenMovie(Movies.MetadataSource source, ItemType itemType, int tmdbId = 0)
        {
            return new Movie
            {
                Tags = new HashSet<int>(),
                MovieMetadata = new MovieMetadata
                {
                    Title = "Title",
                    ItemType = itemType,
                    MetadataSource = source,
                    ForeignId = itemType == ItemType.Scene ? SceneForeignId : tmdbId.ToString(),
                    TmdbId = tmdbId,
                    Images = new List<MediaCover.MediaCover>()
                }
            };
        }

        [TestCase(Movies.MetadataSource.Stash, "StashDB", "https://stashdb.org/scenes/" + SceneForeignId)]
        [TestCase(Movies.MetadataSource.TpdbScene, "TPDB", "https://theporndb.net/scenes/" + SceneForeignId)]
        public void should_link_scene_to_its_source(Movies.MetadataSource source, string siteName, string expectedUrl)
        {
            Subject.OnMovieAdded(GivenMovie(source, ItemType.Scene));

            var embed = _payload.Embeds[0];

            embed.Url.Should().Be(expectedUrl);
            embed.Fields.Should().ContainSingle(f => f.Name == "Links").Which.Value.Should().Be($"[{siteName}]({expectedUrl})");
        }

        [Test]
        public void should_link_movie_to_tmdb_without_a_scene_link()
        {
            Subject.OnMovieAdded(GivenMovie(Movies.MetadataSource.Tmdb, ItemType.Movie, 603));

            var embed = _payload.Embeds[0];

            embed.Url.Should().Be("https://www.themoviedb.org/movie/603");
            embed.Fields.Should().ContainSingle(f => f.Name == "Links").Which.Value.Should().Be("[TMDb](https://www.themoviedb.org/movie/603)");
        }

        [Test]
        public void should_leave_out_links_field_when_movie_has_no_links()
        {
            Subject.OnMovieAdded(GivenMovie(Movies.MetadataSource.TpdbMovie, ItemType.Movie));

            var embed = _payload.Embeds[0];

            embed.Url.Should().BeNull();
            embed.Fields.Should().BeEmpty();
        }
    }
}
