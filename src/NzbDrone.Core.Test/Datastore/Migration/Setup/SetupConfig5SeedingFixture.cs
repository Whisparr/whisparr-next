using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class SetupConfig5SeedingFixture : TestBase
    {
        private string _folder;
        private string _legacyPath;
        private string _config5Path;
        private Config5Seeder _seeder;
        private LegacyInstall _install;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "config5-seeding-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _legacyPath = Path.Combine(_folder, "config.xml");
            _config5Path = Path.Combine(_folder, "config5.xml");
            _seeder = new Config5Seeder(_ => null);
            _install = new LegacyInstall { Kind = LegacyKind.V2, Database = new LegacyDatabaseLocation { PostgresMainDb = "whisparr-main" } };
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        private void WriteLegacy(string authenticationRequired)
        {
            File.WriteAllText(_legacyPath, $"<Config><AuthenticationMethod>Forms</AuthenticationMethod><AuthenticationRequired>{authenticationRequired}</AuthenticationRequired></Config>");
        }

        private string AuthenticationRequired()
        {
            return XDocument.Load(_config5Path).Root.Elements().First(e => e.Name.LocalName == "AuthenticationRequired").Value;
        }

        [Test]
        public void should_seed_when_there_is_no_config5()
        {
            WriteLegacy("Enabled");

            SetupConfig5Seeding.Seed(_seeder, _legacyPath, _config5Path, _install).Should().BeTrue();

            AuthenticationRequired().Should().Be("Enabled");
        }

        [Test]
        public void should_reseed_a_config5_an_earlier_setup_start_seeded_from_a_changed_legacy_config()
        {
            WriteLegacy("Enabled");
            SetupConfig5Seeding.Seed(_seeder, _legacyPath, _config5Path, _install);

            WriteLegacy("DisabledForLocalAddresses");

            SetupConfig5Seeding.Seed(_seeder, _legacyPath, _config5Path, _install).Should().BeTrue();

            AuthenticationRequired().Should().Be("DisabledForLocalAddresses");
        }

        [Test]
        public void should_leave_a_users_own_config5_alone()
        {
            WriteLegacy("Enabled");
            File.WriteAllText(_config5Path, "<Config><AuthenticationRequired>Mine</AuthenticationRequired></Config>");

            SetupConfig5Seeding.Seed(_seeder, _legacyPath, _config5Path, _install).Should().BeFalse();

            AuthenticationRequired().Should().Be("Mine");
        }

        [Test]
        public void should_leave_a_seeded_config5_alone_once_it_was_changed_afterwards()
        {
            WriteLegacy("Enabled");
            SetupConfig5Seeding.Seed(_seeder, _legacyPath, _config5Path, _install);
            File.WriteAllText(_config5Path, "<Config><AuthenticationRequired>Mine</AuthenticationRequired></Config>");

            WriteLegacy("DisabledForLocalAddresses");

            SetupConfig5Seeding.Seed(_seeder, _legacyPath, _config5Path, _install).Should().BeFalse();

            AuthenticationRequired().Should().Be("Mine");
        }

        [Test]
        public void should_keep_a_seeded_config5_when_the_legacy_config_has_gone()
        {
            WriteLegacy("Enabled");
            SetupConfig5Seeding.Seed(_seeder, _legacyPath, _config5Path, _install);
            File.Delete(_legacyPath);

            SetupConfig5Seeding.Seed(_seeder, _legacyPath, _config5Path, _install).Should().BeTrue();

            AuthenticationRequired().Should().Be("Enabled");
        }

        [Test]
        public void should_not_treat_a_config5_as_seeded_once_the_marker_is_cleared()
        {
            WriteLegacy("Enabled");
            SetupConfig5Seeding.Seed(_seeder, _legacyPath, _config5Path, _install);

            SetupConfig5Seeding.ClearMarker(_config5Path);

            SetupConfig5Seeding.IsSetupSeeded(_config5Path).Should().BeFalse();
        }

        [Test]
        public void should_clear_the_marker_when_a_setup_run_completes()
        {
            WriteLegacy("Enabled");
            SetupConfig5Seeding.Seed(_seeder, _legacyPath, _config5Path, _install);

            var appFolder = new Moq.Mock<NzbDrone.Common.EnvironmentInfo.IAppFolderInfo>();
            appFolder.SetupGet(a => a.AppDataFolder).Returns(_folder);

            new SetupRunState(appFolder.Object).Complete();

            SetupConfig5Seeding.IsSetupSeeded(_config5Path).Should().BeFalse();
        }
    }
}
