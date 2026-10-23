using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class Add_file_fingerprintsFixture : MigrationTest<add_file_fingerprints>
    {
        [Test]
        public void should_add_fingerprint_columns_to_movie_files()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("MovieFiles").Row(new
                {
                    MovieId = 1,
                    Quality = "{}",
                    Size = 100L,
                    DateAdded = System.DateTime.UtcNow,
                    RelativePath = "scene.mp4"
                });
            });

            var files = db.Query<MovieFile302>("SELECT \"OsHash\", \"PHash\", \"PHashAttemptedAt\", \"HashedFileModifiedAt\", \"MatchTier\" FROM \"MovieFiles\"");

            files.Should().HaveCount(1);
            var file = files.Single();
            file.OsHash.Should().BeNull();
            file.PHash.Should().BeNull();
            file.PHashAttemptedAt.Should().BeNull();
            file.HashedFileModifiedAt.Should().BeNull();
            file.MatchTier.Should().Be(0);
        }

        [Test]
        public void should_add_fingerprint_columns_to_import_files()
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

            var files = db.Query<ImportFile302>("SELECT \"OsHash\", \"PHash\", \"PHashAttemptedAt\", \"HashedFileModifiedAt\" FROM \"ImportFiles\"");

            files.Should().HaveCount(1);
            var file = files.Single();
            file.OsHash.Should().BeNull();
            file.PHash.Should().BeNull();
            file.PHashAttemptedAt.Should().BeNull();
            file.HashedFileModifiedAt.Should().BeNull();
        }

        [Test]
        public void should_index_movie_files_os_hash()
        {
            var db = WithMigrationTestDb();

            List<IndexName302> indexes;

            try
            {
                indexes = db.Query<IndexName302>("SELECT \"name\" || '' AS \"Name\" FROM sqlite_master WHERE \"type\" = 'index' AND \"tbl_name\" = 'MovieFiles'");
            }
            catch (Exception)
            {
                // PostgreSQL has no sqlite_master
                indexes = db.Query<IndexName302>("SELECT indexname AS \"Name\" FROM pg_indexes WHERE tablename = 'MovieFiles'");
            }

            indexes.Select(x => x.Name).Should().Contain("IX_MovieFiles_OsHash");
        }
    }

    public class IndexName302
    {
        public string Name { get; set; }
    }

    public class MovieFile302
    {
        public string OsHash { get; set; }
        public string PHash { get; set; }
        public System.DateTime? PHashAttemptedAt { get; set; }
        public System.DateTime? HashedFileModifiedAt { get; set; }
        public int MatchTier { get; set; }
    }

    public class ImportFile302
    {
        public string OsHash { get; set; }
        public string PHash { get; set; }
        public System.DateTime? PHashAttemptedAt { get; set; }
        public System.DateTime? HashedFileModifiedAt { get; set; }
    }
}
