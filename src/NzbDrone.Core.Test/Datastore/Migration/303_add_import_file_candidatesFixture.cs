using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class Add_import_file_candidatesFixture : MigrationTest<add_import_file_candidates>
    {
        [Test]
        public void should_add_candidate_columns_to_import_files()
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

            var files = db.Query<ImportFile303>("SELECT \"Candidates\", \"CandidatesCheckedAt\" FROM \"ImportFiles\"");

            files.Should().HaveCount(1);
            var file = files.Single();
            file.Candidates.Should().BeNull();
            file.CandidatesCheckedAt.Should().BeNull();
        }
    }

    public class ImportFile303
    {
        public string Candidates { get; set; }
        public DateTime? CandidatesCheckedAt { get; set; }
    }
}
