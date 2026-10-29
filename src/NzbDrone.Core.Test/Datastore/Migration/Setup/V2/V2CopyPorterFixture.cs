using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Core.Tags;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup.V2
{
    [TestFixture]
    public class V2CopyPorterFixture : V2PorterTestBase
    {
        private long[] V5Ids(string table)
        {
            return V5.Query<long>($"SELECT \"Id\" FROM \"{table}\" ORDER BY \"Id\"", transaction: Transaction).ToArray();
        }

        [Test]
        public void should_copy_tags_root_folders_users_delay_profiles_mappings_keeping_ids()
        {
            InsertV2("Tags", new { Id = 5, Label = "five" });
            InsertV2("Tags", new { Id = 9, Label = "nine" });
            InsertV2("RootFolders", new { Id = 5, Path = "/media/one" });
            InsertV2("RootFolders", new { Id = 9, Path = "/media/two" });
            InsertV2("Users", new { Id = 5, Identifier = "id-5", Username = "alice", Password = "hash", Salt = "salt", Iterations = 10000 });
            InsertV2("Users", new { Id = 9, Identifier = "id-9", Username = "bob", Password = "hash2", Salt = (string)null, Iterations = (int?)null });
            InsertV2("DelayProfiles", new { Id = 5, EnableUsenet = 0, EnableTorrent = 1, PreferredProtocol = 2, UsenetDelay = 3, TorrentDelay = 4, Order = 5, Tags = "[5]", BypassIfHighestQuality = 1, BypassIfAboveCustomFormatScore = 0, MinimumCustomFormatScore = 7 });
            InsertV2("RemotePathMappings", new { Id = 5, Host = "nas", RemotePath = "/remote/", LocalPath = "/local/" });
            InsertV2("RemotePathMappings", new { Id = 9, Host = "nas2", RemotePath = "/remote2/", LocalPath = "/local2/" });

            var before = Sha256(V2Path);

            var context = OpenContext();
            new V2CopyPorter().Port(context);

            V5Ids("Tags").Should().Equal(5, 9);
            V5Ids("RootFolders").Should().Equal(5, 9);
            V5Ids("Users").Should().Equal(5, 9);
            V5Ids("RemotePathMappings").Should().Equal(5, 9);

            V5.ExecuteScalar<string>("SELECT \"Label\" FROM \"Tags\" WHERE \"Id\" = 9", transaction: Transaction).Should().Be("nine");
            V5.ExecuteScalar<string>("SELECT \"Path\" FROM \"RootFolders\" WHERE \"Id\" = 5", transaction: Transaction).Should().Be("/media/one");
            V5.ExecuteScalar<string>("SELECT \"Password\" FROM \"Users\" WHERE \"Id\" = 5", transaction: Transaction).Should().Be("hash");
            V5.ExecuteScalar<int?>("SELECT \"Iterations\" FROM \"Users\" WHERE \"Id\" = 9", transaction: Transaction).Should().BeNull();
            V5.ExecuteScalar<string>("SELECT \"LocalPath\" FROM \"RemotePathMappings\" WHERE \"Id\" = 9", transaction: Transaction).Should().Be("/local2/");

            var profile = V5.QuerySingle<(bool EnableUsenet, bool EnableTorrent, bool BypassIfHighestQuality, int TorrentDelay, string Tags)>(
                "SELECT \"EnableUsenet\", \"EnableTorrent\", \"BypassIfHighestQuality\", \"TorrentDelay\", \"Tags\" FROM \"DelayProfiles\" WHERE \"Id\" = 5",
                transaction: Transaction);

            profile.EnableUsenet.Should().BeFalse();
            profile.EnableTorrent.Should().BeTrue();
            profile.BypassIfHighestQuality.Should().BeTrue();
            profile.TorrentDelay.Should().Be(4);
            profile.Tags.Should().Be("[5]");

            context.InsertedTables.Should().BeEquivalentTo("Tags", "RootFolders", "Users", "DelayProfiles", "RemotePathMappings");

            context.Dispose();
            Sha256(V2Path).Should().Be(before);
        }

        [Test]
        public void should_replace_v5_default_delay_profile()
        {
            ExecuteV2("UPDATE \"DelayProfiles\" SET \"UsenetDelay\" = 99");

            var context = OpenContext();
            new V2CopyPorter().Port(context);

            var profiles = V5.Query<(long Id, int UsenetDelay)>("SELECT \"Id\", \"UsenetDelay\" FROM \"DelayProfiles\"", transaction: Transaction).ToList();

            profiles.Should().ContainSingle();
            profiles.Single().UsenetDelay.Should().Be(99);
        }

        [Test]
        public void should_empty_v5_tables_the_legacy_install_has_no_rows_for()
        {
            V5.Execute("INSERT INTO \"Tags\" (\"Label\") VALUES ('stale')");

            var context = OpenContext();
            new V2CopyPorter().Port(context);

            V5Ids("Tags").Should().BeEmpty();
        }

        [Test]
        public void should_write_the_report()
        {
            var context = OpenContext();
            context.Add(NzbDrone.Core.LegacyMigration.LegacyReportCategory.DroppedToken, "Naming", "StandardSceneFormat", "{Foo}");
            context.Add(NzbDrone.Core.LegacyMigration.LegacyReportCategory.LeftBehind, "Tables", "History", null, 12);

            new V2Porter(new V2CopyPorter(), new V2ConfigPorter(), new V2QualityPorter()).WriteReport(context);

            var rows = V5.Query("SELECT \"Category\", \"Area\", \"Subject\", \"Detail\", \"Count\" FROM \"LegacyMigrationReportItems\" ORDER BY \"Id\"", transaction: Transaction)
                .Cast<System.Collections.Generic.IDictionary<string, object>>()
                .ToList();

            rows.Should().HaveCount(2);
            System.Convert.ToInt32(rows[0]["Category"]).Should().Be(1);
            rows[0]["Detail"].Should().Be("{Foo}");
            System.Convert.ToInt32(rows[1]["Count"]).Should().Be(12);
        }

        [Test]
        public void should_reset_sequences_after_copy()
        {
            if (V5Type != DatabaseType.PostgreSQL)
            {
                Assert.Ignore("PostgreSQL only: Whisparr__Postgres__Host is not set");
            }

            InsertV2("Tags", new { Id = 5, Label = "five" });
            InsertV2("Tags", new { Id = 9, Label = "nine" });

            var context = OpenContext();
            new V2Porter(new V2CopyPorter(), new V2ConfigPorter(), new V2QualityPorter()).PortSettings(context);
            Commit();
            context.Dispose();

            var tag = Mocker.Resolve<TagRepository>().Insert(new Tag { Label = "new" });

            tag.Id.Should().BeGreaterThan(9);
        }
    }
}
