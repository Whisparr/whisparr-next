using System.Linq;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Core.Download;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Notifications;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup.V2
{
    [TestFixture]
    public class V2ProviderPorterFixture : V2PorterTestBase
    {
        private void PortProviders()
        {
            OpenContext();
            new V2ProviderPorter().Port(Context);
        }

        private void InsertDownloadClient(int id, string name, string implementation, string settings)
        {
            InsertV2("DownloadClients", new
            {
                Id = id,
                Enable = 1,
                Name = name,
                Implementation = implementation,
                Settings = settings,
                ConfigContract = implementation + "Settings",
                Priority = 1,
                RemoveCompletedDownloads = 1,
                RemoveFailedDownloads = 1,
                Tags = "[]"
            });
        }

        private void InsertIndexer(int id, string name, string implementation, string settings, int downloadClientId = 0)
        {
            InsertV2("Indexers", new
            {
                Id = id,
                EnableRss = 1,
                EnableAutomaticSearch = 1,
                EnableInteractiveSearch = 1,
                Name = name,
                Implementation = implementation,
                Settings = settings,
                ConfigContract = implementation + "Settings",
                Priority = 25,
                Tags = "[]",
                DownloadClientId = downloadClientId,
                SeasonSearchMaximumSingleEpisodeAge = 7
            });
        }

        private void InsertNotification(int id, string name, string implementation, string settings, object extra = null)
        {
            InsertV2("Notifications", new
            {
                Id = id,
                Name = name,
                OnGrab = 1,
                OnDownload = 1,
                Settings = settings,
                Implementation = implementation,
                ConfigContract = implementation + "Settings",
                OnUpgrade = 1,
                Tags = "[]",
                OnRename = 0,
                OnHealthIssue = 0,
                IncludeHealthWarnings = 0,
                OnSeriesDelete = 1,
                OnEpisodeFileDelete = 0,
                OnEpisodeFileDeleteForUpgrade = 1,
                OnApplicationUpdate = 0,
                OnSeriesAdd = 1,
                OnHealthRestored = 0,
                OnManualInteractionRequired = 0,
                OnImportComplete = extra == null ? 0 : 1
            });
        }

        private JsonObject V5Settings(string table, int id)
        {
            var json = V5.ExecuteScalar<string>($"SELECT \"Settings\" FROM \"{table}\" WHERE \"Id\" = @id", new { id }, Transaction);

            return JsonNode.Parse(json).AsObject();
        }

        [Test]
        public void should_rename_download_client_tv_keys()
        {
            InsertDownloadClient(
                1,
                "SAB",
                "Sabnzbd",
                "{\"host\":\"h\",\"tvCategory\":\"tv\",\"recentTvPriority\":-100,\"olderTvPriority\":2}");
            InsertDownloadClient(
                2,
                "qB",
                "QBittorrent",
                "{\"host\":\"h\",\"tvCategory\":\"c\",\"tvImportedCategory\":\"i\",\"tvDirectory\":\"/d\",\"unknownKey\":5}");

            PortProviders();

            var sab = V5Settings("DownloadClients", 1);
            sab["movieCategory"].GetValue<string>().Should().Be("tv");
            sab["recentMoviePriority"].GetValue<int>().Should().Be(-100);
            sab["olderMoviePriority"].GetValue<int>().Should().Be(2);
            sab.ContainsKey("tvCategory").Should().BeFalse();
            sab.ContainsKey("recentTvPriority").Should().BeFalse();
            sab.ContainsKey("olderTvPriority").Should().BeFalse();

            var qb = V5Settings("DownloadClients", 2);
            qb["movieCategory"].GetValue<string>().Should().Be("c");
            qb["movieImportedCategory"].GetValue<string>().Should().Be("i");
            qb["movieDirectory"].GetValue<string>().Should().Be("/d");
            qb.ContainsKey("tvDirectory").Should().BeFalse();
            qb["unknownKey"].GetValue<int>().Should().Be(5);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void should_invert_flood_start_on_add(bool startOnAdd, bool expectedAddPaused)
        {
            InsertDownloadClient(1, "Flood", "Flood", $"{{\"host\":\"h\",\"startOnAdd\":{startOnAdd.ToString().ToLowerInvariant()}}}");

            PortProviders();

            var settings = V5Settings("DownloadClients", 1);
            settings["addPaused"].GetValue<bool>().Should().Be(expectedAddPaused);
            settings.ContainsKey("startOnAdd").Should().BeFalse();
        }

        [Test]
        public void should_leave_flood_settings_alone_without_start_on_add()
        {
            InsertDownloadClient(1, "Flood", "Flood", "{\"host\":\"h\",\"addPaused\":true}");

            PortProviders();

            V5Settings("DownloadClients", 1)["addPaused"].GetValue<bool>().Should().BeTrue();
        }

        [Test]
        public void should_skip_and_report_rqbit_tribler_fanzub()
        {
            InsertDownloadClient(1, "RQ", "RQBit", "{}");
            InsertDownloadClient(2, "Tri", "Tribler", "{}");
            InsertDownloadClient(3, "SAB", "Sabnzbd", "{}");
            InsertIndexer(1, "Fan", "Fanzub", "{}");
            InsertIndexer(2, "Nab", "Newznab", "{}");

            PortProviders();

            V5.Query<int>("SELECT \"Id\" FROM \"DownloadClients\"", transaction: Transaction).Should().BeEquivalentTo(new[] { 3 });
            V5.Query<int>("SELECT \"Id\" FROM \"Indexers\"", transaction: Transaction).Should().BeEquivalentTo(new[] { 2 });

            var skipped = ReportFor(LegacyReportCategory.SkippedSetting);
            skipped.Should().Contain(r => r.Area == "DownloadClients" && r.Subject == "RQ");
            skipped.Should().Contain(r => r.Area == "DownloadClients" && r.Subject == "Tri");
            skipped.Should().Contain(r => r.Area == "Indexers" && r.Subject == "Fan");
        }

        [Test]
        public void should_clear_indexer_download_client_pointing_at_skipped_client()
        {
            InsertDownloadClient(1, "RQ", "RQBit", "{}");
            InsertDownloadClient(2, "SAB", "Sabnzbd", "{}");
            InsertIndexer(1, "ToSkipped", "Newznab", "{}", 1);
            InsertIndexer(2, "ToKept", "Newznab", "{}", 2);

            PortProviders();

            V5.ExecuteScalar<int>("SELECT \"DownloadClientId\" FROM \"Indexers\" WHERE \"Id\" = 1", transaction: Transaction).Should().Be(0);
            V5.ExecuteScalar<int>("SELECT \"DownloadClientId\" FROM \"Indexers\" WHERE \"Id\" = 2", transaction: Transaction).Should().Be(2);

            ReportFor(LegacyReportCategory.NeedsAttention).Should().ContainSingle(r => r.Area == "Indexers" && r.Subject == "ToSkipped");
        }

        [TestCase("Newznab")]
        [TestCase("Torznab")]
        public void should_drop_v2_only_indexer_keys(string implementation)
        {
            InsertIndexer(
                1,
                "Idx",
                implementation,
                "{\"baseUrl\":\"u\",\"seriesNameSource\":1,\"searchSiteTitleOnly\":true,\"searchTitleOnly\":true,\"searchExternalIdOnly\":true,\"dateSearchFormat\":\"x\",\"apiKey\":\"k\"}");
            InsertIndexer(2, "Other", "Fanzubx", "{\"searchTitleOnly\":true}");

            PortProviders();

            var settings = V5Settings("Indexers", 1);
            settings["baseUrl"].GetValue<string>().Should().Be("u");
            settings["apiKey"].GetValue<string>().Should().Be("k");

            foreach (var key in new[] { "seriesNameSource", "searchSiteTitleOnly", "searchTitleOnly", "searchExternalIdOnly", "dateSearchFormat" })
            {
                settings.ContainsKey(key).Should().BeFalse(key);
            }

            V5Settings("Indexers", 2).ContainsKey("searchTitleOnly").Should().BeTrue();
        }

        [Test]
        public void should_map_notification_event_columns()
        {
            InsertNotification(1, "N", "Webhook", "{}");

            PortProviders();

            var row = (System.Collections.Generic.IDictionary<string, object>)V5.Query("SELECT * FROM \"Notifications\" WHERE \"Id\" = 1", transaction: Transaction).Single();

            System.Convert.ToBoolean(row["OnMovieDelete"]).Should().BeTrue();
            System.Convert.ToBoolean(row["OnMovieFileDelete"]).Should().BeFalse();
            System.Convert.ToBoolean(row["OnMovieFileDeleteForUpgrade"]).Should().BeTrue();
            System.Convert.ToBoolean(row["OnMovieAdded"]).Should().BeTrue();
            System.Convert.ToBoolean(row["OnGrab"]).Should().BeTrue();
            System.Convert.ToBoolean(row["OnUpgrade"]).Should().BeTrue();
            row["Name"].Should().Be("N");
        }

        [TestCase(true, 1)]
        [TestCase(false, 0)]
        public void should_convert_email_encryption(bool requireEncryption, int expected)
        {
            InsertNotification(1, "Mail", "Email", $"{{\"server\":\"s\",\"requireEncryption\":{requireEncryption.ToString().ToLowerInvariant()}}}");

            PortProviders();

            var settings = V5Settings("Notifications", 1);
            settings["useEncryption"].GetValue<int>().Should().Be(expected);
            settings.ContainsKey("requireEncryption").Should().BeFalse();
        }

        [Test]
        public void should_map_gotify_poster_and_drop_telegram_link_preview()
        {
            InsertNotification(1, "Go", "Gotify", "{\"includeSeriesPoster\":true}");
            InsertNotification(2, "Tg", "Telegram", "{\"botToken\":\"t\",\"linkPreview\":true}");

            PortProviders();

            var gotify = V5Settings("Notifications", 1);
            gotify["includeMoviePoster"].GetValue<bool>().Should().BeTrue();
            gotify.ContainsKey("includeSeriesPoster").Should().BeFalse();

            var telegram = V5Settings("Notifications", 2);
            telegram.ContainsKey("linkPreview").Should().BeFalse();
            telegram["botToken"].GetValue<string>().Should().Be("t");
        }

        [Test]
        public void should_report_on_import_complete()
        {
            InsertNotification(1, "Imp", "Webhook", "{}", new { });
            InsertNotification(2, "NoImp", "Webhook", "{}");

            PortProviders();

            var skipped = ReportFor(LegacyReportCategory.SkippedSetting);
            skipped.Should().ContainSingle(r => r.Area == "Notifications" && r.Subject == "Imp");
            skipped.Should().NotContain(r => r.Area == "Notifications" && r.Subject == "NoImp");
        }

        [Test]
        public void should_map_metadata_settings_and_drop_plex()
        {
            // The v2 dump's four default rows: Xbmc, Wdtv, Roksbox, Plex.
            ExecuteV2("UPDATE \"Metadata\" SET \"Enable\" = 1 WHERE \"Implementation\" IN ('XbmcMetadata', 'PlexMetadata')");
            ExecuteV2(
                "UPDATE \"Metadata\" SET \"Settings\" = @settings WHERE \"Implementation\" = 'XbmcMetadata'",
                new { settings = "{\"seriesMetadata\":true,\"seriesMetadataUrl\":true,\"episodeMetadata\":false,\"episodeImages\":true,\"seasonImages\":true}" });

            PortProviders();

            var rows = V5.Query("SELECT \"Id\", \"Enable\", \"Name\", \"Implementation\", \"ConfigContract\", \"Settings\" FROM \"Metadata\" ORDER BY \"Id\"", transaction: Transaction)
                .Cast<System.Collections.Generic.IDictionary<string, object>>()
                .ToList();

            rows.Select(r => (string)r["Implementation"]).Should().BeEquivalentTo(new[] { "XbmcMetadata", "WdtvMetadata", "RoksboxMetadata" });
            rows.Should().OnlyContain(r => r["Implementation"].ToString() != "PlexMetadata");

            var xbmc = rows.Single(r => (string)r["Implementation"] == "XbmcMetadata");
            System.Convert.ToBoolean(xbmc["Enable"]).Should().BeTrue();
            xbmc["Name"].Should().Be("Kodi (XBMC) / Emby");
            xbmc["ConfigContract"].Should().Be("XbmcMetadataSettings");

            var xbmcSettings = JsonNode.Parse((string)xbmc["Settings"]).AsObject();
            xbmcSettings["movieMetadata"].GetValue<bool>().Should().BeFalse();
            xbmcSettings["movieImages"].GetValue<bool>().Should().BeTrue();
            xbmcSettings["movieMetadataURL"].GetValue<bool>().Should().BeTrue();
            xbmcSettings.ContainsKey("seriesMetadata").Should().BeFalse();
            xbmcSettings.ContainsKey("episodeMetadata").Should().BeFalse();

            var wdtvSettings = JsonNode.Parse((string)rows.Single(r => (string)r["Implementation"] == "WdtvMetadata")["Settings"]).AsObject();
            wdtvSettings["movieMetadata"].GetValue<bool>().Should().BeTrue();
            wdtvSettings["movieImages"].GetValue<bool>().Should().BeTrue();
            wdtvSettings.ContainsKey("movieMetadataURL").Should().BeFalse();

            // Plex was enabled, so it is reported.
            ReportFor(LegacyReportCategory.SkippedSetting).Should().ContainSingle(r => r.Area == "Metadata" && r.Subject == "Plex");
        }

        [Test]
        public void should_not_report_disabled_plex_metadata()
        {
            PortProviders();

            ReportFor(LegacyReportCategory.SkippedSetting).Should().NotContain(r => r.Area == "Metadata");
        }

        [Test]
        public void ported_providers_should_load_through_v5_factories()
        {
            InsertDownloadClient(1, "SAB", "Sabnzbd", "{\"host\":\"h\",\"port\":8080,\"tvCategory\":\"tv\"}");
            InsertDownloadClient(2, "Flood", "Flood", "{\"host\":\"h\",\"port\":3000,\"startOnAdd\":true}");
            InsertDownloadClient(3, "qB", "QBittorrent", "{\"host\":\"h\",\"port\":8080,\"tvCategory\":\"tv\"}");
            InsertIndexer(1, "Nab", "Newznab", "{\"baseUrl\":\"http://n\",\"searchTitleOnly\":true}", 1);
            InsertIndexer(2, "Tor", "Torznab", "{\"baseUrl\":\"http://t\",\"dateSearchFormat\":\"x\"}");
            InsertNotification(1, "Mail", "Email", "{\"server\":\"s\",\"port\":25,\"requireEncryption\":true}");
            InsertNotification(2, "Go", "Gotify", "{\"server\":\"s\",\"appToken\":\"a\",\"includeSeriesPoster\":true}");

            PortProviders();
            Commit();

            Mocker.Resolve<IndexerRepository>().All().Should().HaveCount(2);
            Mocker.Resolve<DownloadClientRepository>().All().Should().HaveCount(3);
            Mocker.Resolve<NotificationRepository>().All().Should().HaveCount(2);
            Mocker.Resolve<MetadataRepository>().All().Should().HaveCount(3);
        }
    }
}
