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

                c.Insert.IntoTable("NamingConfig").Row(new
                {
                    RenameMovies = false,
                    RenameScenes = false,
                    ReplaceIllegalCharacters = true,
                    ColonReplacementFormat = 0
                });
            });

            var studio = db.Query<Studio314>("SELECT \"JavMonitored\", \"HasJav\", \"JavCount\", \"TotalJavCount\" FROM \"Studios\"").Single();
            studio.JavMonitored.Should().BeFalse();
            studio.HasJav.Should().BeFalse();
            studio.JavCount.Should().Be(0);
            studio.TotalJavCount.Should().Be(0);

            var performer = db.Query<Studio314>("SELECT \"JavMonitored\", \"HasJav\", \"JavCount\", \"TotalJavCount\" FROM \"Performers\"").Single();
            performer.JavMonitored.Should().BeFalse();
            performer.HasJav.Should().BeFalse();
            performer.JavCount.Should().Be(0);
            performer.TotalJavCount.Should().Be(0);

            var naming = db.Query<Naming314>("SELECT \"RenameJav\", \"StandardJavFormat\", \"JavFolderFormat\" FROM \"NamingConfig\"").Single();
            naming.RenameJav.Should().BeFalse("it copies RenameScenes, which the install had off");
            naming.StandardJavFormat.Should().Be("{Scene Code} - {Scene CleanTitle} {[Quality Title]}");
            naming.JavFolderFormat.Should().Be(OsInfo.IsWindows ? "jav\\{Studio CleanTitle}\\{Scene Code}" : "jav/{Studio CleanTitle}/{Scene Code}");
        }
    }

    public class Studio314
    {
        public bool JavMonitored { get; set; }
        public bool HasJav { get; set; }
        public int JavCount { get; set; }
        public int TotalJavCount { get; set; }
    }

    public class Naming314
    {
        public bool RenameJav { get; set; }
        public string StandardJavFormat { get; set; }
        public string JavFolderFormat { get; set; }
    }
}
