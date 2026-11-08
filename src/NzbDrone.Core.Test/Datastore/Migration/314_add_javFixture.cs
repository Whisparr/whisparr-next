using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class Add_javFixture : MigrationTest<add_jav>
    {
        [Test]
        public void should_add_jav_columns_and_default_formats()
        {
            var db = WithMigrationTestDb(c =>
            {
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

                c.Insert.IntoTable("NamingConfig").Row(new
                {
                    RenameMovies = false,
                    RenameScenes = false,
                    ReplaceIllegalCharacters = true,
                    ColonReplacementFormat = 0
                });
            });

            var studio = db.Query<Studio314>("SELECT \"JavMonitored\", \"HasJav\" FROM \"Studios\"").Single();
            studio.JavMonitored.Should().BeFalse();
            studio.HasJav.Should().BeFalse();

            var naming = db.Query<Naming314>("SELECT \"RenameJav\", \"StandardJavFormat\", \"JavFolderFormat\" FROM \"NamingConfig\"").Single();
            naming.RenameJav.Should().BeTrue();
            naming.StandardJavFormat.Should().Be("{Scene Code} - {Scene CleanTitle} {[Quality Title]}");
            naming.JavFolderFormat.Should().Be(OsInfo.IsWindows ? "jav\\{Studio CleanTitle}\\{Scene Code}" : "jav/{Studio CleanTitle}/{Scene Code}");
        }
    }

    public class Studio314
    {
        public bool JavMonitored { get; set; }
        public bool HasJav { get; set; }
    }

    public class Naming314
    {
        public bool RenameJav { get; set; }
        public string StandardJavFormat { get; set; }
        public string JavFolderFormat { get; set; }
    }
}
