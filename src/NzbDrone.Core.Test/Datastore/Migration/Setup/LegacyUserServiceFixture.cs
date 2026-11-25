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
            InsertUser(_dbPath, "user", Password);
        }

        private static void InsertUser(string path, string username, string password)
        {
            var salt = new byte[16];
            var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 10000, HashAlgorithmName.SHA512, 32);

            using var connection = new System.Data.SQLite.SQLiteConnection(new System.Data.SQLite.SQLiteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
            connection.Open();
            connection.Execute(
                "INSERT INTO \"Users\" (\"Identifier\", \"Username\", \"Password\", \"Salt\", \"Iterations\") VALUES (@id, @username, @password, @salt, 10000)",
                new { id = Guid.NewGuid().ToString(), username, password = Convert.ToBase64String(hash), salt = Convert.ToBase64String(salt) });
        }

        private LegacyInstall V2Install(string username, string password)
        {
            var path = Path.Combine(TempFolder, "whisparr2.db");
            LegacyDbBuilder.CreateV2SqliteFromDump(path);
            InsertUser(path, username, password);

            return new LegacyInstall
            {
                Kind = LegacyKind.V2,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = path }
            };
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
        public void should_find_users_from_both_installs_when_both_exist()
        {
            // An Eros boot on a v2 config dir creates its own user, which must not hide the v2 one.
            var service = GivenService(V2Install("v2user", "v2pass"), ErosInstall(_dbPath));

            service.FindUser("user", Password).Username.Should().Be("user");
            service.FindUser("v2user", "v2pass").Username.Should().Be("v2user");
            service.FindUser("v2user", Password).Should().BeNull();
        }

        [Test]
        public void should_find_v2_user_by_identifier_when_both_installs_exist()
        {
            var service = GivenService(V2Install("v2user", "v2pass"), ErosInstall(_dbPath));
            var user = service.FindUser("v2user", "v2pass");

            service.FindUser(user.Identifier).Username.Should().Be("v2user");
        }

        [Test]
        public void should_accept_either_password_when_both_installs_share_a_username()
        {
            var service = GivenService(V2Install("user", "v2pass"), ErosInstall(_dbPath));

            service.FindUser("user", Password).Should().NotBeNull();
            service.FindUser("user", "v2pass").Should().NotBeNull();
            service.FindUser("user", "wrong").Should().BeNull();
        }

        [Test]
        public void should_list_eros_user_first_when_both_installs_exist()
        {
            var service = GivenService(V2Install("v2user", "v2pass"), ErosInstall(_dbPath));

            service.FindUser().Username.Should().Be("user");
        }

        [Test]
        public void should_still_find_v2_user_when_eros_db_is_unreadable()
        {
            var service = GivenService(V2Install("v2user", "v2pass"), ErosInstall(Path.Combine(TempFolder, "missing.db")));

            service.FindUser("v2user", "v2pass").Should().NotBeNull();
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
