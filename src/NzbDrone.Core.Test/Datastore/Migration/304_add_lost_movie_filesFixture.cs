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
    public class Add_lost_movie_filesFixture : MigrationTest<add_lost_movie_files>
    {
        [Test]
        public void should_create_lost_movie_files_table()
        {
            var db = WithMigrationTestDb();

            // Selecting every column proves the table and its columns exist
            var rows = db.Query<LostMovieFile304>("SELECT \"Id\", \"MovieId\", \"OsHash\", \"Size\", \"RelativePath\", \"LostAt\" FROM \"LostMovieFiles\"");

            rows.Should().BeEmpty();
        }

        [Test]
        public void should_index_movie_id_and_os_hash()
        {
            var db = WithMigrationTestDb();

            List<IndexName304> indexes;

            try
            {
                indexes = db.Query<IndexName304>("SELECT \"name\" || '' AS \"Name\" FROM sqlite_master WHERE \"type\" = 'index' AND \"tbl_name\" = 'LostMovieFiles'");
            }
            catch (Exception)
            {
                // PostgreSQL has no sqlite_master
                indexes = db.Query<IndexName304>("SELECT indexname AS \"Name\" FROM pg_indexes WHERE tablename = 'LostMovieFiles'");
            }

            indexes.Select(x => x.Name).Should().Contain("IX_LostMovieFiles_MovieId");
            indexes.Select(x => x.Name).Should().Contain("IX_LostMovieFiles_OsHash");
        }
    }

    public class IndexName304
    {
        public string Name { get; set; }
    }

    public class LostMovieFile304
    {
        public int Id { get; set; }
        public int MovieId { get; set; }
        public string OsHash { get; set; }
        public long Size { get; set; }
        public string RelativePath { get; set; }
        public DateTime LostAt { get; set; }
    }
}
