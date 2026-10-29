using System;
using System.IO;
using System.Security.Cryptography;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup
{
    [TestFixture]
    public class SetupModeProxySettingsProviderFixture : TestBase
    {
        private string _dbPath;

        [SetUp]
        public void Setup()
        {
            _dbPath = Path.Combine(TempFolder, "whisparr3.db");
            LegacyDbBuilder.CreateErosSqlite(_dbPath);
        }

        private static LegacyInstall Install(LegacyKind kind, string path)
        {
            return new LegacyInstall
            {
                Kind = kind,
                Database = new LegacyDatabaseLocation { Type = DatabaseType.SQLite, SqlitePath = path }
            };
        }

        private static SetupModeProxySettingsProvider Provider(params LegacyInstall[] installs)
        {
            var detection = new DetectionResult { SetupModeRequired = true };
            detection.Installs.AddRange(installs);

            return new SetupModeProxySettingsProvider(new SetupModeState(detection, false));
        }

        private static void GivenProxy(string path, string enabled = "True", string host = "proxy.example.test", string port = "3128")
        {
            LegacyDbBuilder.InsertConfig(path, "proxyenabled", enabled);
            LegacyDbBuilder.InsertConfig(path, "proxytype", "socks5");
            LegacyDbBuilder.InsertConfig(path, "proxyhostname", host);
            LegacyDbBuilder.InsertConfig(path, "proxyport", port);
            LegacyDbBuilder.InsertConfig(path, "proxyusername", "me");
            LegacyDbBuilder.InsertConfig(path, "proxypassword", "secret");
            LegacyDbBuilder.InsertConfig(path, "proxybypassfilter", "*.internal.test");
            LegacyDbBuilder.InsertConfig(path, "proxybypasslocaladdresses", "True");
        }

        private static string Sha256(string path)
        {
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        }

        [Test]
        public void should_read_the_enabled_proxy_from_the_legacy_config()
        {
            GivenProxy(_dbPath);

            var before = Sha256(_dbPath);

            var settings = Provider(Install(LegacyKind.Eros, _dbPath)).GetProxySettings();

            settings.Should().NotBeNull();
            settings.Type.Should().Be(ProxyType.Socks5);
            settings.Host.Should().Be("proxy.example.test");
            settings.Port.Should().Be(3128);
            settings.Username.Should().Be("me");
            settings.Password.Should().Be("secret");
            settings.BypassFilter.Should().Be("*.internal.test");
            settings.BypassLocalAddress.Should().BeTrue();

            Sha256(_dbPath).Should().Be(before);
        }

        [Test]
        public void should_return_null_when_the_proxy_is_disabled()
        {
            GivenProxy(_dbPath, enabled: "False");

            Provider(Install(LegacyKind.Eros, _dbPath)).GetProxySettings().Should().BeNull();
        }

        [Test]
        public void should_return_null_without_a_proxy_config()
        {
            Provider(Install(LegacyKind.Eros, _dbPath)).GetProxySettings().Should().BeNull();
        }

        [Test]
        public void should_return_null_without_installs()
        {
            Provider().GetProxySettings().Should().BeNull();
        }

        [Test]
        public void should_return_null_when_the_legacy_database_cannot_be_read()
        {
            var missing = Path.Combine(TempFolder, "missing.db");

            Provider(Install(LegacyKind.Eros, missing)).GetProxySettings().Should().BeNull();
        }

        [Test]
        public void should_prefer_the_eros_install()
        {
            var v2Path = Path.Combine(TempFolder, "whisparr.db");
            LegacyDbBuilder.CreateV2SqliteFromDump(v2Path);
            GivenProxy(v2Path, host: "v2.example.test");
            GivenProxy(_dbPath, host: "eros.example.test");

            var settings = Provider(Install(LegacyKind.V2, v2Path), Install(LegacyKind.Eros, _dbPath)).GetProxySettings();

            settings.Host.Should().Be("eros.example.test");
        }

        [Test]
        public void should_use_the_only_install_when_it_is_v2()
        {
            var v2Path = Path.Combine(TempFolder, "whisparr.db");
            LegacyDbBuilder.CreateV2SqliteFromDump(v2Path);
            GivenProxy(v2Path, host: "v2.example.test");

            Provider(Install(LegacyKind.V2, v2Path)).GetProxySettings().Host.Should().Be("v2.example.test");
        }

        [Test]
        public void should_read_the_legacy_database_once()
        {
            GivenProxy(_dbPath);

            var provider = Provider(Install(LegacyKind.Eros, _dbPath));

            provider.GetProxySettings().Should().NotBeNull();

            File.Delete(_dbPath);

            provider.GetProxySettings().Should().NotBeNull();
        }

        [Test]
        public void should_bypass_hosts_matching_the_bypass_filter()
        {
            GivenProxy(_dbPath);

            var provider = Provider(Install(LegacyKind.Eros, _dbPath));

            provider.GetProxySettings(new HttpUri("https://api.internal.test/route")).Should().BeNull();
            provider.GetProxySettings(new HttpUri("https://api.example.com/route")).Should().NotBeNull();
        }
    }
}
