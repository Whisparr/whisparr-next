using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class Config5SeederFixture : TestBase
    {
        private string _folder;
        private string _legacyPath;
        private string _config5Path;
        private Dictionary<string, string> _env;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "config5-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            _legacyPath = Path.Combine(_folder, "config.xml");
            _config5Path = Path.Combine(_folder, "config5.xml");
            _env = new Dictionary<string, string>();
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_folder, true);
        }

        private Config5Seeder Subject => new(name => _env.TryGetValue(name, out var value) ? value : null);

        private static LegacyInstall Install(string mainDb = "whisparr-main")
        {
            return new LegacyInstall
            {
                Kind = LegacyKind.V2,
                Database = new LegacyDatabaseLocation { PostgresMainDb = mainDb }
            };
        }

        private void WriteLegacy(string body)
        {
            File.WriteAllText(_legacyPath, "<Config>" + body + "</Config>");
        }

        private Dictionary<string, string> ReadV5()
        {
            return XDocument.Load(_config5Path).Root.Elements().ToDictionary(e => e.Name.LocalName, e => e.Value);
        }

        [Test]
        public void should_copy_every_key()
        {
            WriteLegacy("<Port>6969</Port><ApiKey>abc</ApiKey><SslKeyPath>/k.pem</SslKeyPath><Custom>x</Custom>");

            Subject.Seed(_legacyPath, _config5Path, Install());

            var values = ReadV5();

            values["Port"].Should().Be("6969");
            values["ApiKey"].Should().Be("abc");
            values["SslKeyPath"].Should().Be("/k.pem");
            values["Custom"].Should().Be("x");
        }

        [Test]
        public void should_set_postgres_names_to_v5_defaults()
        {
            WriteLegacy("<PostgresMainDb>whisparr-main</PostgresMainDb><PostgresLogDb>whisparr-log</PostgresLogDb>");

            var targets = Subject.Seed(_legacyPath, _config5Path, Install());

            targets.MainDb.Should().Be("whisparr5-main");
            targets.LogDb.Should().Be("whisparr5-log");
            targets.EnvironmentStillNamesLegacy.Should().BeFalse();

            var values = ReadV5();

            values["PostgresMainDb"].Should().Be("whisparr5-main");
            values["PostgresLogDb"].Should().Be("whisparr5-log");
        }

        [Test]
        public void should_keep_env_named_non_legacy_target()
        {
            WriteLegacy("<Port>6969</Port>");
            _env["Whisparr__Postgres__MainDb"] = "mine-main";
            _env["Whisparr__Postgres__LogDb"] = "mine-log";

            var targets = Subject.Seed(_legacyPath, _config5Path, Install());

            targets.MainDb.Should().Be("mine-main");
            targets.LogDb.Should().Be("mine-log");
            targets.EnvironmentStillNamesLegacy.Should().BeFalse();
            targets.EnvVarsStillNamingLegacy.Should().NotBeNull().And.BeEmpty();
            ReadV5()["PostgresMainDb"].Should().Be("mine-main");
        }

        [TestCase("Whisparr__Postgres__MainDb", "whisparr-main")]
        [TestCase("Whisparr__Postgres__LogDb", "whisparr-log")]
        public void should_flag_env_still_naming_legacy(string variable, string value)
        {
            WriteLegacy("<Port>6969</Port>");
            _env[variable] = value;

            var targets = Subject.Seed(_legacyPath, _config5Path, Install());

            targets.EnvironmentStillNamesLegacy.Should().BeTrue();
            targets.EnvVarsStillNamingLegacy.Should().Equal(variable);
            targets.MainDb.Should().Be("whisparr5-main");
            targets.LogDb.Should().Be("whisparr5-log");
        }

        [TestCase("Whisparr__Postgres__MainDbConnectionString", "whisparr-main")]
        [TestCase("Whisparr__Postgres__LogDbConnectionString", "whisparr-log")]
        public void should_flag_env_connection_string_still_naming_legacy(string variable, string database)
        {
            WriteLegacy("<Port>6969</Port>");
            _env[variable] = "Host=db;Username=u;Database=" + database;

            var targets = Subject.Seed(_legacyPath, _config5Path, Install());

            targets.EnvironmentStillNamesLegacy.Should().BeTrue();
            targets.EnvVarsStillNamingLegacy.Should().Equal(variable);
        }

        [Test]
        public void should_not_flag_env_connection_string_naming_other_database()
        {
            WriteLegacy("<Port>6969</Port>");
            _env["Whisparr__Postgres__MainDbConnectionString"] = "Host=db;Database=mine";
            _env["Whisparr__Postgres__LogDbConnectionString"] = "garbage;;=";

            var targets = Subject.Seed(_legacyPath, _config5Path, Install());

            targets.EnvironmentStillNamesLegacy.Should().BeFalse();
            targets.EnvVarsStillNamingLegacy.Should().BeEmpty();
        }

        [Test]
        public void should_throw_for_missing_legacy_config()
        {
            var action = () => Subject.Seed(_legacyPath, _config5Path, Install());

            action.Should().Throw<InvalidConfigFileException>().WithMessage("*config.xml*could not be read*");
        }

        [Test]
        public void should_throw_for_malformed_legacy_config()
        {
            File.WriteAllText(_legacyPath, "<Config><Port>");

            var action = () => Subject.Seed(_legacyPath, _config5Path, Install());

            action.Should().Throw<InvalidConfigFileException>().WithMessage("*config.xml*could not be read*");
            File.Exists(_config5Path).Should().BeFalse();
        }

        [Test]
        public void should_rewrite_connection_string_database()
        {
            WriteLegacy("<PostgresMainDbConnectionString>Host=db;Port=5432;Username=u;Database=whisparr-main</PostgresMainDbConnectionString>" +
                        "<PostgresLogDbConnectionString>Host=db;Port=5432;Username=u;Database=whisparr-log</PostgresLogDbConnectionString>");

            var targets = Subject.Seed(_legacyPath, _config5Path, Install());
            var values = ReadV5();

            targets.EnvironmentStillNamesLegacy.Should().BeFalse();
            values["PostgresMainDbConnectionString"].Should().Contain("Database=whisparr5-main").And.Contain("Host=db");
            values["PostgresLogDbConnectionString"].Should().Contain("Database=whisparr5-log");
        }

        [Test]
        public void should_leave_unparseable_connection_string_alone()
        {
            WriteLegacy("<PostgresMainDbConnectionString>not a;;=connection string</PostgresMainDbConnectionString>");

            Subject.Seed(_legacyPath, _config5Path, Install(null));

            ReadV5()["PostgresMainDbConnectionString"].Should().Be("not a;;=connection string");
        }

        [Test]
        public void should_apply_v2_key_renames()
        {
            // The v2/v5 diff found no renames (v2-only: SslKeyPath; v5-only: ConsoleLogFormat, LogDbEnabled).
            var renames = (Dictionary<string, string>)typeof(Config5Seeder)
                .GetField("V2KeyRenames", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

            renames.Should().BeEmpty();
        }

        [Test]
        public void should_not_modify_legacy_config()
        {
            WriteLegacy("<Port>6969</Port><PostgresMainDb>whisparr-main</PostgresMainDb>");
            var before = SHA256.HashData(File.ReadAllBytes(_legacyPath));

            Subject.Seed(_legacyPath, _config5Path, Install());

            SHA256.HashData(File.ReadAllBytes(_legacyPath)).Should().Equal(before);
            Directory.GetFiles(_folder, "*.tmp").Should().BeEmpty();
        }

        [Test]
        public void should_overwrite_existing_config5()
        {
            WriteLegacy("<Port>6969</Port>");
            File.WriteAllText(_config5Path, "<Config><Port>1</Port></Config>");

            Subject.Seed(_legacyPath, _config5Path, Install());

            ReadV5()["Port"].Should().Be("6969");
        }

        [Test]
        public void plan_targets_should_name_env_targets_without_writing_config5()
        {
            WriteLegacy("<Port>6969</Port>");
            _env["Whisparr__Postgres__MainDb"] = "whisparr-main";

            var targets = Subject.PlanTargets(_legacyPath, Install());

            targets.EnvVarsStillNamingLegacy.Should().Equal("Whisparr__Postgres__MainDb");
            targets.EnvTargets.Should().ContainSingle();
            targets.EnvTargets[0].Name.Should().Be("Whisparr__Postgres__MainDb");
            targets.EnvTargets[0].Value.Should().Be(Config5Seeder.V5MainDb);
            File.Exists(_config5Path).Should().BeFalse();
        }

        [Test]
        public void should_work_with_default_environment_access()
        {
            WriteLegacy("<Port>6969</Port>");

            new Config5Seeder().Seed(_legacyPath, _config5Path, null);

            ReadV5().Should().ContainKey("PostgresMainDb");
        }
    }
}
