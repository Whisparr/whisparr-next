using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class Lock_metadata_sources_for_existing_librariesFixture : MigrationTest<lock_metadata_sources_for_existing_libraries>
    {
        [Test]
        public void should_lock_when_library_has_movies()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("MovieMetadata").Row(new
                {
                    ForeignId = "scene-1",
                    MetadataSource = 2,
                    Images = "[]",
                    Title = "Scene",
                    OriginalLanguage = 1,
                    Status = 1,
                    Runtime = 0,
                    Recommendations = "[]",
                    ItemType = 0
                });

                c.Insert.IntoTable("Movies").Row(new
                {
                    Path = "/library/scene",
                    Monitored = true,
                    QualityProfileId = 1,
                    Added = System.DateTime.UtcNow,
                    MovieMetadataId = 1
                });
            });

            var config = db.Query<Config301>("SELECT \"Key\" || '' AS \"Key\", \"Value\" || '' AS \"Value\" FROM \"Config\"")
                           .ToDictionary(x => x.Key, x => x.Value);

            config.Should().ContainKey("whisparrscenemetadatasource").WhoseValue.Should().Be("stashdb");
            config.Should().ContainKey("metadatasourceslocked").WhoseValue.Should().Be("true");
            config.Should().NotContainKey("whisparrmoviemetadatasource");
        }

        [Test]
        public void should_leave_unlocked_when_library_empty()
        {
            var db = WithMigrationTestDb();

            var keys = db.Query<Config301>("SELECT \"Key\" || '' AS \"Key\", \"Value\" || '' AS \"Value\" FROM \"Config\"")
                         .Select(x => x.Key);

            keys.Should().NotContain("whisparrscenemetadatasource");
            keys.Should().NotContain("metadatasourceslocked");
        }
    }

    public class Config301
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
