using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.MediaFiles.LostFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class CleanupExpiredLostMovieFilesFixture : DbTest<CleanupExpiredLostMovieFiles, LostMovieFile>
    {
        private static LostMovieFile Entry(int movieId, int daysAgo)
        {
            return new LostMovieFile
            {
                MovieId = movieId,
                OsHash = "aaaaaaaaaaaaaaaa",
                Size = 1234,
                RelativePath = "scene.mkv",
                LostAt = DateTime.UtcNow.AddDays(-daysAgo)
            };
        }

        [Test]
        public void should_delete_entries_older_than_thirty_days()
        {
            Db.Insert(Entry(1, 31));

            Subject.Clean();

            AllStoredModels.Should().BeEmpty();
        }

        [Test]
        public void should_keep_entries_inside_thirty_days()
        {
            Db.Insert(Entry(1, 29));

            Subject.Clean();

            AllStoredModels.Should().HaveCount(1);
        }
    }
}
