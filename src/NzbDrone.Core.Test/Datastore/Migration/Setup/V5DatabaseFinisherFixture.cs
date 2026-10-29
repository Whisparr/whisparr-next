using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class V5DatabaseFinisherFixture : DbTest
    {
        private IDbConnection _connection;

        [SetUp]
        public void Setup()
        {
            _connection = Mocker.Resolve<IMainDatabase>().OpenConnection();

            // Read back through the real repositories on this DB, not auto-mocks
            Mocker.SetConstant<IConfigRepository>(Mocker.Resolve<ConfigRepository>());
            Mocker.SetConstant<INamingConfigRepository>(Mocker.Resolve<NamingConfigRepository>());

            // ConfigService keeps a static cache that outlives each test's database; the finisher writes around it
            // exactly as it will in setup mode, so start every test with it empty.
            typeof(ConfigService)
                .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, new Dictionary<string, string>());
        }

        [TearDown]
        public void Teardown()
        {
            _connection?.Dispose();
        }

        private static LegacyMigrationState State() => new()
        {
            SourceKind = LegacyKind.Eros,
            SceneSource = SceneMetadataType.Tpdb,
            MovieSource = MovieMetadataType.TPDB,
            Phase = "setup",
            Progress = "{}",
            Completed = false,
            StartedAt = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc),
            BackupPath = "~/backup"
        };

        private void Finish(SceneMetadataType scene, MovieMetadataType movie, LegacyMigrationState state = null)
        {
            using var tx = _connection.BeginTransaction();

            new V5DatabaseFinisher().Finish(_connection, tx, scene, movie, state ?? State());

            tx.Commit();
        }

        [Test]
        public void finisher_should_lock_sources_and_disable_renaming()
        {
            _connection.Execute("INSERT INTO \"NamingConfig\" (\"ReplaceIllegalCharacters\", \"ColonReplacementFormat\", \"RenameMovies\", \"RenameScenes\") VALUES (@on, 0, @on, @on)", new { on = true });

            Finish(SceneMetadataType.Tpdb, MovieMetadataType.TPDB);

            var config = Mocker.Resolve<ConfigService>();
            config.MetadataSourcesLocked.Should().BeTrue();
            config.WhisparrSceneMetadataSource.Should().Be(SceneMetadataType.Tpdb);
            config.WhisparrMovieMetadataSource.Should().Be(MovieMetadataType.TPDB);

            var naming = Mocker.Resolve<NamingConfigService>().GetConfig();
            naming.RenameScenes.Should().BeFalse();
            naming.RenameMovies.Should().BeFalse();
        }

        [Test]
        public void finisher_should_turn_off_auto_rename_folders_copied_from_eros()
        {
            _connection.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('autorenamefolders', 'True')");

            Finish(SceneMetadataType.Tpdb, MovieMetadataType.TPDB);

            Mocker.Resolve<ConfigService>().AutoRenameFolders.Should().BeFalse();
            _connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"Config\" WHERE \"Key\" = 'autorenamefolders'").Should().Be(1);
        }

        [Test]
        public void finisher_should_write_auto_rename_folders_off_when_the_source_never_set_it()
        {
            Finish(SceneMetadataType.Tpdb, MovieMetadataType.TPDB);

            _connection.ExecuteScalar<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = 'autorenamefolders'").Should().Be("False");
            Mocker.Resolve<ConfigService>().AutoRenameFolders.Should().BeFalse();
        }

        [Test]
        public void finisher_should_overwrite_stashdb_lock_from_301_with_choice()
        {
            _connection.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('whisparrscenemetadatasource', 'stashdb')");
            _connection.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('metadatasourceslocked', 'True')");

            Finish(SceneMetadataType.Tpdb, MovieMetadataType.TMDB);

            var config = Mocker.Resolve<ConfigService>();
            config.WhisparrSceneMetadataSource.Should().Be(SceneMetadataType.Tpdb);
            config.WhisparrMovieMetadataSource.Should().Be(MovieMetadataType.TMDB);
            config.MetadataSourcesLocked.Should().BeTrue();
            _connection.ExecuteScalar<int>("SELECT COUNT(*) FROM \"Config\" WHERE \"Key\" = 'whisparrscenemetadatasource'").Should().Be(1);
        }

        [Test]
        public void finisher_should_write_state_row()
        {
            Finish(SceneMetadataType.Tpdb, MovieMetadataType.TPDB);

            var row = Mocker.Resolve<LegacyMigrationStateRepository>().GetCurrent();

            row.SourceKind.Should().Be(LegacyKind.Eros);
            row.SceneSource.Should().Be(SceneMetadataType.Tpdb);
            row.MovieSource.Should().Be(MovieMetadataType.TPDB);
            row.Phase.Should().Be("setup");
            row.Progress.Should().Be("{}");
            row.Completed.Should().BeFalse();
            row.StartedAt.Should().NotBeNull();
            row.CompletedAt.Should().BeNull();
            row.BackupPath.Should().Be("~/backup");
        }
    }
}
