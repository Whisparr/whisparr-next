using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class Add_tpdb_numeric_idFixture : MigrationTest<add_tpdb_numeric_id>
    {
        [Test]
        public void should_add_nullable_numeric_id_columns_to_existing_rows()
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

                c.Insert.IntoTable("Studios").Row(new
                {
                    ForeignId = "studio-foreign-id",
                    QualityProfileId = 1,
                    RootFolderPath = "/movies",
                    SearchOnAdd = false,
                    Title = "Existing Studio",
                    CleanTitle = "existingstudio",
                    Images = "[]",
                    Monitored = false,
                    MoviesMonitored = false,
                    Status = 0,
                    TmdbId = 0,
                    MovieCount = 0,
                    SceneCount = 0,
                    TotalMovieCount = 0,
                    TotalSceneCount = 0,
                    SizeOnDisk = 0,
                    Tags = "[]"
                });

                c.Insert.IntoTable("Performers").Row(new
                {
                    ForeignId = "performer-foreign-id",
                    QualityProfileId = 1,
                    RootFolderPath = "/movies",
                    SearchOnAdd = false,
                    Name = "Existing Performer",
                    CleanName = "existingperformer",
                    Gender = 0,
                    Status = 0,
                    Tags = "[]"
                });
            });

            db.Query<NumericId316>("SELECT \"TpdbNumericId\" FROM \"MovieMetadata\"").Single().TpdbNumericId.Should().BeNull();
            db.Query<NumericId316>("SELECT \"TpdbNumericId\" FROM \"Studios\"").Single().TpdbNumericId.Should().BeNull();
            db.Query<NumericId316>("SELECT \"TpdbNumericId\" FROM \"Performers\"").Single().TpdbNumericId.Should().BeNull();
        }
    }

    public class NumericId316
    {
        public int? TpdbNumericId { get; set; }
    }
}
