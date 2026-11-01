using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class Add_import_file_top_matchFixture : MigrationTest<add_import_file_top_match>
    {
        [Test]
        public void should_add_top_match_columns_to_import_files()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("ImportFiles").Row(new
                {
                    RootFolderId = 1,
                    Path = "/library/scene.mp4",
                    RelativePath = "scene.mp4",
                    Name = "scene"
                });
            });

            var files = db.Query<ImportFile313>("SELECT \"TopMatch\", \"TopMatchCheckedAt\" FROM \"ImportFiles\"");

            files.Should().HaveCount(1);
            var file = files.Single();
            file.TopMatch.Should().BeNull();
            file.TopMatchCheckedAt.Should().BeNull();
        }
    }

    public class ImportFile313
    {
        public string TopMatch { get; set; }
        public DateTime? TopMatchCheckedAt { get; set; }
    }
}
