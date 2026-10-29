using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class LegacyUserServiceFixture : TestBase
    {
        private const string Password = "hunter2";

        private string _dbPath;

        [SetUp]
        public void Setup()
        {
            _dbPath = Path.Combine(TempFolder, "whisparr3.db");
            LegacyDbBuilder.CreateErosSqlite(_dbPath);

            // 10000 iterations is below v5's 220000, so a rehash-on-login would write.
            var salt = new byte[16];
            var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(Password), salt, 10000, HashAlgorithmName.SHA512, 32);

            using var connection = new System.Data.SQLite.SQLiteConnection(new System.Data.SQLite.SQLiteConnectionStringBuilder { DataSource = _dbPath, Pooling = false }.ConnectionString);
            connection.Open();
            connection.Execute(
                "INSERT INTO \"Users\" (\"Identifier\", \"Username\", \"Password\", \"Salt\", \"Iterations\") VALUES (@id, 'user', @password, @salt, 10000)",
                new { id = Guid.NewGuid().ToString(), password = Convert.ToBase64String(hash), salt = Convert.ToBase64String(salt) });
        }

        private LegacyUserService GivenService(params LegacyInstall[] installs)
        {
            var detection = new DetectionResult { SetupModeRequired = true };
            detection.Installs.AddRange(installs);

            return new LegacyUserService(new SetupModeState(detection, false));
        }

        private LegacyInstall ErosInstall(string path)
        {
            return new LegacyInstall
            {
                Kind = LegacyKind.Eros,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = path }
            };
        }

        private static string Sha256(string path)
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        [Test]
        public void should_read_users_from_the_user_source_for_a_pending_env_switch()
        {
            // Installs stays empty for a pending env switch, and the copied Users live in the v5 main database.
            var detection = new DetectionResult
            {
                SetupModeRequired = true,
                PendingEnvSwitch = true,
                UserSource = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = _dbPath }
            };

            var service = new LegacyUserService(new SetupModeState(detection, false));

            service.FindUser("user", Password).Should().NotBeNull();
            service.FindUser("user", "wrong").Should().BeNull();
            service.FindUser().Should().NotBeNull();
        }

        [Test]
        public void should_find_nobody_for_a_pending_env_switch_without_a_user_source()
        {
            var detection = new DetectionResult { SetupModeRequired = true, PendingEnvSwitch = true };

            new LegacyUserService(new SetupModeState(detection, false)).FindUser("user", Password).Should().BeNull();
        }

        [Test]
        public void should_find_user_with_correct_password()
        {
            var user = GivenService(ErosInstall(_dbPath)).FindUser("USER", Password);

            user.Should().NotBeNull();
            user.Username.Should().Be("user");
            user.Identifier.Should().NotBeEmpty();
        }

        [Test]
        public void should_not_find_user_with_wrong_password_or_name()
        {
            var service = GivenService(ErosInstall(_dbPath));

            service.FindUser("user", "wrong").Should().BeNull();
            service.FindUser("other", Password).Should().BeNull();
            service.FindUser("user", "").Should().BeNull();
        }

        [Test]
        public void should_find_user_by_identifier_and_single()
        {
            var service = GivenService(ErosInstall(_dbPath));
            var user = service.FindUser();

            user.Should().NotBeNull();
            service.FindUser(user.Identifier).Should().NotBeNull();
            service.FindUser(Guid.NewGuid()).Should().BeNull();
        }

        [Test]
        public void should_not_write_to_legacy_db_on_login()
        {
            var before = Sha256(_dbPath);

            GivenService(ErosInstall(_dbPath)).FindUser("user", Password).Should().NotBeNull();

            Sha256(_dbPath).Should().Be(before);
        }

        [Test]
        public void should_prefer_eros_when_both_installs_exist()
        {
            var v2Path = Path.Combine(TempFolder, "whisparr2.db");
            LegacyDbBuilder.CreateV2Sqlite(v2Path, 221);

            var v2 = new LegacyInstall
            {
                Kind = LegacyKind.V2,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = v2Path }
            };

            GivenService(v2, ErosInstall(_dbPath)).FindUser("user", Password).Should().NotBeNull();
        }

        [Test]
        public void should_have_no_users_without_installs_or_when_db_is_unreadable()
        {
            GivenService().FindUser().Should().BeNull();
            GivenService().FindUser("user", Password).Should().BeNull();
            GivenService(ErosInstall(Path.Combine(TempFolder, "missing.db"))).FindUser("user", Password).Should().BeNull();
        }

        [Test]
        public void should_throw_on_add()
        {
            var service = GivenService(ErosInstall(_dbPath));

            Assert.Throws<InvalidOperationException>(() => service.Add("user", Password));
            Assert.Throws<InvalidOperationException>(() => service.Update(new User()));
            Assert.Throws<InvalidOperationException>(() => service.Upsert("user", Password));
        }
    }
}
