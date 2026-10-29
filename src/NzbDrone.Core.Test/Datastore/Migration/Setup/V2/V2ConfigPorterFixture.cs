using System.Collections.Generic;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup.V2
{
    [TestFixture]
    public class V2ConfigPorterFixture : V2PorterTestBase
    {
        private string ConfigValue(string key)
        {
            return V5.ExecuteScalar<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = @key", new { key }, Transaction);
        }

        private void PortConfig()
        {
            new V2ConfigPorter().Port(OpenContext());
        }

        private void InsertNaming(string episodeFormat, string seriesFormat, int colon = 4, string customColon = "", string jav = null)
        {
            InsertV2("NamingConfig", new
            {
                MultiEpisodeStyle = 5,
                RenameEpisodes = 1,
                StandardEpisodeFormat = episodeFormat,
                SeriesFolderFormat = seriesFormat,
                ReplaceIllegalCharacters = 0,
                ColonReplacementFormat = colon,
                JavEpisodeFormat = jav,
                CustomColonReplacementFormat = customColon
            });
        }

        private (string Standard, string Folder, bool Replace, int Colon, bool RenameScenes, bool RenameMovies) Naming()
        {
            var row = V5.QuerySingle(
                "SELECT \"StandardSceneFormat\", \"SceneFolderFormat\", \"ReplaceIllegalCharacters\", \"ColonReplacementFormat\", \"RenameScenes\", \"RenameMovies\" FROM \"NamingConfig\"",
                transaction: Transaction);

            var values = (IDictionary<string, object>)row;

            return (
                (string)values["StandardSceneFormat"],
                (string)values["SceneFolderFormat"],
                System.Convert.ToBoolean(values["ReplaceIllegalCharacters"]),
                System.Convert.ToInt32(values["ColonReplacementFormat"]),
                System.Convert.ToBoolean(values["RenameScenes"]),
                System.Convert.ToBoolean(values["RenameMovies"]));
        }

        [Test]
        public void should_copy_config_keys()
        {
            InsertV2("Config", new { Key = "loglevel", Value = "debug" });
            InsertV2("Config", new { Key = "ENABLEAUTOMATICSEARCH", Value = "x" });
            V5.Execute("INSERT INTO \"Config\" (\"Key\", \"Value\") VALUES ('loglevel', 'info')", transaction: null);

            var before = Sha256(V2Path);

            PortConfig();

            ConfigValue("loglevel").Should().Be("debug");
            ConfigValue("enableautomaticsearch").Should().Be("x");
            V5.ExecuteScalar<int>("SELECT COUNT(*) FROM \"Config\" WHERE lower(\"Key\") = 'loglevel'", transaction: Transaction).Should().Be(1);

            Context.Dispose();
            Sha256(V2Path).Should().Be(before);
        }

        [Test]
        public void should_rename_series_keys()
        {
            InsertV2("Config", new { Key = "autounmonitorpreviouslydownloadedepisodes", Value = "True" });
            InsertV2("Config", new { Key = "createemptyseriesfolders", Value = "True" });

            PortConfig();

            ConfigValue("autounmonitorpreviouslydownloadedmovies").Should().Be("True");
            ConfigValue("createemptymoviefolders").Should().Be("True");
            ConfigValue("autounmonitorpreviouslydownloadedepisodes").Should().BeNull();
            ConfigValue("createemptyseriesfolders").Should().BeNull();
        }

        [Test]
        public void should_drop_and_report_v2_only_keys()
        {
            InsertV2("Config", new { Key = "episodetitlerequired", Value = "1" });
            InsertV2("Config", new { Key = "userrejectedextensions", Value = ".exe" });

            PortConfig();

            ConfigValue("episodetitlerequired").Should().BeNull();
            ConfigValue("userrejectedextensions").Should().BeNull();

            var skipped = ReportFor(LegacyReportCategory.SkippedSetting);

            skipped.Select(r => r.Subject).Should().BeEquivalentTo("episodetitlerequired", "userrejectedextensions");
            skipped.Should().OnlyContain(r => r.Area == "Config");
        }

        [Test]
        public void should_port_v2_defaults_when_no_naming_row()
        {
            PortConfig();

            var naming = Naming();

            naming.Folder.Should().Be("{Studio Title}");
            naming.Standard.Should().Be("{Studio Title} - {Release-Date} - {Scene Title} [{Quality Full}]");
            naming.Colon.Should().Be(4);
            naming.Replace.Should().BeTrue();
            Context.Report.Should().BeEmpty();
        }

        [Test]
        public void should_port_legacy_naming_row()
        {
            InsertNaming("{Site Title} - {Episode Title}", "{Site Title}/{Site Network}", colon: 2);

            PortConfig();

            var naming = Naming();

            naming.Standard.Should().Be("{Studio Title} - {Scene Title}");
            naming.Folder.Should().Be("{Studio Title}/{Studio Network}");
            naming.Replace.Should().BeFalse();
            naming.Colon.Should().Be(2);
        }

        [Test]
        public void should_take_the_remaining_naming_values_from_the_v5_defaults()
        {
            PortConfig();

            var defaults = NamingConfig.Default;
            var row = (IDictionary<string, object>)V5.QuerySingle("SELECT * FROM \"NamingConfig\"", transaction: Transaction);

            row["StandardMovieFormat"].Should().Be(defaults.StandardMovieFormat);
            row["MovieFolderFormat"].Should().Be(defaults.MovieFolderFormat);
            row["SceneImportFolderFormat"].Should().Be(defaults.SceneImportFolderFormat);
            System.Convert.ToInt32(row["MaxFolderPathLength"]).Should().Be(defaults.MaxFolderPathLength);
            System.Convert.ToInt32(row["MaxFilePathLength"]).Should().Be(defaults.MaxFilePathLength);
        }

        [Test]
        public void should_map_custom_colon_to_smart_and_report()
        {
            InsertNaming("{Site Title}", "{Site Title}", colon: 5, customColon: "~");

            PortConfig();

            Naming().Colon.Should().Be(4);

            var skipped = ReportFor(LegacyReportCategory.SkippedSetting);

            skipped.Should().ContainSingle();
            skipped.Single().Detail.Should().Be("~");
            skipped.Single().Area.Should().Be("Naming");
        }

        [Test]
        public void should_disable_renaming()
        {
            InsertNaming("{Site Title}", "{Site Title}");

            PortConfig();

            var naming = Naming();

            naming.RenameScenes.Should().BeFalse();
            naming.RenameMovies.Should().BeFalse();
        }

        [Test]
        public void should_report_dropped_tokens()
        {
            InsertNaming("{Site Title} {Nonsense Token}", "{Site Title}");

            PortConfig();

            var dropped = ReportFor(LegacyReportCategory.DroppedToken);

            dropped.Should().ContainSingle();
            dropped.Single().Area.Should().Be("Naming");
            dropped.Single().Subject.Should().Be("StandardSceneFormat");
            dropped.Single().Detail.Should().Contain("Nonsense Token");
        }

        [Test]
        public void should_use_default_folder_and_flag_when_mapped_folder_format_is_empty()
        {
            InsertNaming("{Site Title}", string.Empty);

            PortConfig();

            Naming().Folder.Should().Be("{Studio Title}");
            ReportFor(LegacyReportCategory.NeedsAttention).Should().ContainSingle();
        }

        [Test]
        public void should_report_a_jav_format()
        {
            InsertNaming("{Site Title}", "{Site Title}", jav: "{External Id} - {Site Title}");

            PortConfig();

            var skipped = ReportFor(LegacyReportCategory.SkippedSetting);

            skipped.Should().ContainSingle();
            skipped.Single().Area.Should().Be("Naming");
            skipped.Single().Detail.Should().Be("{External Id} - {Site Title}");
        }

        [Test]
        public void should_not_report_the_v2_default_jav_format()
        {
            InsertNaming("{Site Title}", "{Site Title}", jav: " {External Id} ");

            PortConfig();

            ReportFor(LegacyReportCategory.SkippedSetting).Should().BeEmpty();
        }

        [Test]
        public void should_report_a_customised_jav_format()
        {
            InsertNaming("{Site Title}", "{Site Title}", jav: "{External Id} custom");

            PortConfig();

            ReportFor(LegacyReportCategory.SkippedSetting).Should().ContainSingle().Which.Detail.Should().Be("{External Id} custom");
        }

        [Test]
        public void should_use_default_standard_format_and_flag_when_it_is_empty()
        {
            InsertNaming(string.Empty, "{Site Title}");

            PortConfig();

            Naming().Standard.Should().Be("{Studio Title} - {Release-Date} - {Scene Title} [{Quality Full}]");

            var flagged = ReportFor(LegacyReportCategory.NeedsAttention);

            flagged.Should().ContainSingle();
            flagged.Single().Area.Should().Be("Naming");
            flagged.Single().Subject.Should().Be("StandardSceneFormat");
        }

        [Test]
        public void should_replace_existing_v5_naming_rows()
        {
            V5.Execute("INSERT INTO \"NamingConfig\" (\"ReplaceIllegalCharacters\", \"ColonReplacementFormat\", \"RenameMovies\", \"RenameScenes\") VALUES (@on, 0, @on, @on)", new { on = true });

            PortConfig();

            V5.ExecuteScalar<int>("SELECT COUNT(*) FROM \"NamingConfig\"", transaction: Transaction).Should().Be(1);
        }

        [Test]
        public void naming_should_read_back_through_v5_service()
        {
            InsertNaming("{Site Title} - {Episode Title}", "{Site Title}", colon: 1);

            PortConfig();
            Commit();
            Context.Dispose();

            Mocker.SetConstant<INamingConfigRepository>(Mocker.Resolve<NamingConfigRepository>());

            var config = Mocker.Resolve<NamingConfigService>().GetConfig();

            config.StandardSceneFormat.Should().Be("{Studio Title} - {Scene Title}");
            config.SceneFolderFormat.Should().Be("{Studio Title}");
            config.ColonReplacementFormat.Should().Be(ColonReplacementFormat.Dash);
            config.ReplaceIllegalCharacters.Should().BeFalse();
            config.RenameScenes.Should().BeFalse();
            config.RenameMovies.Should().BeFalse();
            config.StandardMovieFormat.Should().Be(NamingConfig.Default.StandardMovieFormat);
        }
    }
}
