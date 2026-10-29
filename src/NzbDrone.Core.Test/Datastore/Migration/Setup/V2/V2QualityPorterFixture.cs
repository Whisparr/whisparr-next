using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore.Migration.Setup.V2;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Test.Datastore.Migration.Setup.V2
{
    [TestFixture]
    public class V2QualityPorterFixture : V2PorterTestBase
    {
        private void PortQuality()
        {
            OpenContext();
            new V2QualityPorter().Port(Context);
        }

        private void InsertProfile(int id, string name, int cutoff, string items, string formatItems = "[]")
        {
            InsertV2("QualityProfiles", new
            {
                Id = id,
                Name = name,
                Cutoff = cutoff,
                Items = items,
                UpgradeAllowed = 1,
                FormatItems = formatItems,
                MinFormatScore = 3,
                CutoffFormatScore = 4,
                MinUpgradeFormatScore = 5
            });
        }

        private void InsertSeries(int id, int profileId)
        {
            ExecuteV2(
                "INSERT INTO \"Series\" (\"Id\", \"TvdbId\", \"Title\", \"CleanTitle\", \"Status\", \"Images\", \"Path\", \"Monitored\", \"QualityProfileId\", \"Runtime\", \"UseSceneNumbering\", \"OriginalLanguage\") VALUES (@id, @id, 'S', 's', 0, '[]', '/s', 1, @profileId, 0, 0, 1)",
                new { id, profileId });
        }

        private JsonArray V5Items(int profileId)
        {
            return JsonNode.Parse(V5.ExecuteScalar<string>("SELECT \"Items\" FROM \"QualityProfiles\" WHERE \"Id\" = @profileId", new { profileId }, Transaction)).AsArray();
        }

        private static List<int> Flatten(JsonArray items)
        {
            var result = new List<int>();

            foreach (var item in items)
            {
                if (item["quality"] != null)
                {
                    result.Add(item["quality"].GetValue<int>());
                }

                if (item["items"] is JsonArray children)
                {
                    result.AddRange(Flatten(children));
                }
            }

            return result;
        }

        private const string VrItems = "[{\"quality\":22,\"items\":[],\"allowed\":true,\"minSize\":4,\"maxSize\":9,\"preferredSize\":95}]";

        [Test]
        public void should_remap_profile_quality_ids_and_cutoff()
        {
            InsertProfile(20, "VR only", 22, VrItems);

            PortQuality();

            V5.ExecuteScalar<int>("SELECT \"Cutoff\" FROM \"QualityProfiles\" WHERE \"Id\" = 20", transaction: Transaction).Should().Be(32);

            var items = V5Items(20);
            var vr = items.Single(i => i["quality"]?.GetValue<int>() == 32);

            vr["allowed"].GetValue<bool>().Should().BeTrue();
            vr["minSize"].Should().BeNull();
            vr["maxSize"].Should().BeNull();
            vr["preferredSize"].Should().BeNull();

            // 22 is a different quality in v5, so it comes back as a missing quality, disallowed.
            items.Single(i => i["quality"]?.GetValue<int>() == 22)["allowed"].GetValue<bool>().Should().BeFalse();
        }

        [Test]
        public void should_keep_group_cutoff_and_remap_quality_objects()
        {
            InsertProfile(
                21,
                "Group",
                1002,
                "[{\"id\":1002,\"name\":\"G\",\"items\":[{\"quality\":{\"id\":13,\"name\":\"x\"},\"items\":[],\"allowed\":true},{\"quality\":23,\"items\":[],\"allowed\":true}],\"allowed\":true}]");

            PortQuality();

            V5.ExecuteScalar<int>("SELECT \"Cutoff\" FROM \"QualityProfiles\" WHERE \"Id\" = 21", transaction: Transaction).Should().Be(1002);

            var group = V5Items(21).Single(i => i["id"]?.GetValue<int>() == 1002);

            group["items"].AsArray().Select(i => i["quality"].GetValue<int>()).Should().Equal(20, 21);
        }

        [Test]
        public void should_add_missing_v5_qualities_disallowed_at_start()
        {
            InsertProfile(20, "VR only", 22, VrItems);

            PortQuality();

            var items = V5Items(20);

            items.Count.Should().Be(Quality.All.Count);
            items[items.Count - 1]["quality"].GetValue<int>().Should().Be(32);
            items.Take(items.Count - 1).Should().OnlyContain(i => !i["allowed"].GetValue<bool>());
            Flatten(items).Order().Should().Equal(Quality.All.Select(q => q.Id).Order());
        }

        [Test]
        public void ported_profiles_should_load_through_v5_repository()
        {
            PortQuality();
            Commit();
            Context.Dispose();

            Mocker.GetMock<ICustomFormatService>().Setup(s => s.All()).Returns(new List<CustomFormat>());

            var profiles = Mocker.Resolve<QualityProfileRepository>().All().ToList();

            profiles.Should().HaveCount(7);

            foreach (var profile in profiles)
            {
                profile.Items.SelectMany(i => i.GetQualities()).Select(q => q.Id).Order()
                    .Should().Equal(Quality.All.Select(q => q.Id).Order(), profile.Name);
                profile.Language.Id.Should().Be(-1);
                profile.Fallback.Should().BeFalse();
                profile.MinUpgradeFormatScore.Should().Be(1);
            }
        }

        [Test]
        public void should_copy_profile_scores_and_upgrade_flag()
        {
            InsertProfile(20, "VR only", 22, VrItems);

            PortQuality();

            var row = V5.QuerySingle<(int Min, int Cutoff, int MinUpgrade, bool UpgradeAllowed)>(
                "SELECT \"MinFormatScore\" AS Min, \"CutoffFormatScore\" AS Cutoff, \"MinUpgradeFormatScore\" AS MinUpgrade, \"UpgradeAllowed\" AS UpgradeAllowed FROM \"QualityProfiles\" WHERE \"Id\" = 20",
                transaction: Transaction);

            row.Should().Be((3, 4, 5, true));
        }

        [Test]
        public void should_leave_no_ranks_for_startup_seeding()
        {
            PortQuality();

            V5.ExecuteScalar<int>("SELECT COUNT(*) FROM \"QualityProfileQualityRanks\"", transaction: Transaction).Should().Be(0);
        }

        [Test]
        public void should_take_sizes_from_most_used_profile()
        {
            InsertProfile(20, "Big", 22, "[{\"quality\":4,\"items\":[],\"allowed\":true,\"minSize\":77,\"maxSize\":88,\"preferredSize\":66}]");
            InsertSeries(1, 20);
            InsertSeries(2, 20);
            InsertSeries(3, 1);

            PortQuality();

            var row = V5.QuerySingle<(double? Min, double? Max, double? Preferred)>(
                "SELECT \"MinSize\" AS Min, \"MaxSize\" AS Max, \"PreferredSize\" AS Preferred FROM \"QualityDefinitions\" WHERE \"Quality\" = 4",
                transaction: Transaction);

            row.Should().Be((77d, 88d, 66d));

            V5.ExecuteScalar<int>("SELECT COUNT(*) FROM \"QualityDefinitions\"", transaction: Transaction).Should().Be(23);

            // quality 5 is not in Big, so no sizes
            V5.ExecuteScalar<double?>("SELECT \"MinSize\" FROM \"QualityDefinitions\" WHERE \"Quality\" = 5", transaction: Transaction).Should().BeNull();

            ReportFor(LegacyReportCategory.SkippedSetting)
                .Where(r => r.Area == "Quality")
                .Should().ContainSingle(r => r.Detail == "Sizes taken from profile Big");
        }

        [Test]
        public void should_take_sizes_from_lowest_id_profile_without_series_and_not_report_equal_sizes()
        {
            PortQuality();

            // profile 1 (Any): quality 4 min 3 max 125
            V5.ExecuteScalar<double?>("SELECT \"MinSize\" FROM \"QualityDefinitions\" WHERE \"Quality\" = 4", transaction: Transaction).Should().Be(3);
            V5.ExecuteScalar<double?>("SELECT \"MaxSize\" FROM \"QualityDefinitions\" WHERE \"Quality\" = 4", transaction: Transaction).Should().Be(125);
            ReportFor(LegacyReportCategory.SkippedSetting).Where(r => r.Area == "Quality").Should().BeEmpty();
        }

        [Test]
        public void should_remap_definition_quality_ids_and_titles()
        {
            PortQuality();

            var titles = V5.Query<(int Quality, string Title)>("SELECT \"Quality\", \"Title\" FROM \"QualityDefinitions\"", transaction: Transaction)
                .ToDictionary(r => r.Quality, r => r.Title);

            titles[20].Should().Be(Quality.FindById(20).Name);
            titles[32].Should().Be(Quality.FindById(32).Name);
            titles[1].Should().Be("SDTV");
            titles.Keys.Should().NotContain(22);
        }

        [Test]
        public void should_remap_source_language_and_flag_specs()
        {
            InsertV2("CustomFormats", new
            {
                Id = 7,
                Name = "Mixed",
                Specifications = "[{\"type\":\"SourceSpecification\",\"body\":{\"name\":\"s\",\"negate\":false,\"required\":false,\"value\":7}}," +
                                 "{\"type\":\"LanguageSpecification\",\"body\":{\"name\":\"l\",\"negate\":false,\"required\":false,\"value\":50}}," +
                                 "{\"type\":\"IndexerFlagSpecification\",\"body\":{\"name\":\"f\",\"negate\":false,\"required\":false,\"value\":16}}," +
                                 "{\"type\":\"ReleaseTitleSpecification\",\"body\":{\"name\":\"t\",\"negate\":false,\"required\":false,\"value\":\"abc\"}}]",
                IncludeCustomFormatWhenRenaming = 1
            });

            PortQuality();
            Commit();
            Context.Dispose();

            var format = Mocker.Resolve<CustomFormatRepository>().All().Single();

            format.Id.Should().Be(7);
            format.IncludeCustomFormatWhenRenaming.Should().BeTrue();
            format.Specifications.Should().HaveCount(4);

            var specs = format.Specifications.Select(s => s.GetType().Name).ToList();
            specs.Should().Equal("SourceSpecification", "LanguageSpecification", "IndexerFlagSpecification", "ReleaseTitleSpecification");
        }

        [Test]
        public void should_store_remapped_spec_values()
        {
            InsertV2("CustomFormats", new
            {
                Id = 7,
                Name = "Mixed",
                Specifications = "[{\"type\":\"SourceSpecification\",\"body\":{\"name\":\"s\",\"value\":7}},{\"type\":\"LanguageSpecification\",\"body\":{\"name\":\"l\",\"value\":50}},{\"type\":\"IndexerFlagSpecification\",\"body\":{\"name\":\"f\",\"value\":16}}]",
                IncludeCustomFormatWhenRenaming = 0
            });

            PortQuality();

            var specs = JsonNode.Parse(V5.ExecuteScalar<string>("SELECT \"Specifications\" FROM \"CustomFormats\" WHERE \"Id\" = 7", transaction: Transaction)).AsArray();

            specs[0]["body"]["value"].GetValue<int>().Should().Be(10);
            specs[1]["body"]["value"].GetValue<int>().Should().Be(NzbDrone.Core.Languages.Language.Urdu.Id);
            specs[2]["body"]["value"].GetValue<int>().Should().Be(128);
        }

        [Test]
        public void should_drop_and_report_unmappable_language_spec()
        {
            InsertV2("CustomFormats", new
            {
                Id = 8,
                Name = "Azeri or title",
                Specifications = "[{\"type\":\"LanguageSpecification\",\"body\":{\"name\":\"az\",\"value\":47}},{\"type\":\"ReleaseTitleSpecification\",\"body\":{\"name\":\"t\",\"value\":\"abc\"}}]",
                IncludeCustomFormatWhenRenaming = 0
            });

            PortQuality();

            var specs = JsonNode.Parse(V5.ExecuteScalar<string>("SELECT \"Specifications\" FROM \"CustomFormats\" WHERE \"Id\" = 8", transaction: Transaction)).AsArray();

            specs.Should().HaveCount(1);
            specs[0]["type"].GetValue<string>().Should().Be("ReleaseTitleSpecification");

            ReportFor(LegacyReportCategory.SkippedSetting).Should().ContainSingle(r => r.Area == "CustomFormats" && r.Subject == "Azeri or title");
        }

        [Test]
        public void should_skip_empty_format_and_remove_it_from_profiles()
        {
            InsertV2("CustomFormats", new
            {
                Id = 8,
                Name = "Azeri",
                Specifications = "[{\"type\":\"LanguageSpecification\",\"body\":{\"name\":\"az\",\"value\":47}}]",
                IncludeCustomFormatWhenRenaming = 0
            });
            InsertV2("CustomFormats", new
            {
                Id = 9,
                Name = "Kept",
                Specifications = "[{\"type\":\"ReleaseTitleSpecification\",\"body\":{\"name\":\"t\",\"value\":\"abc\"}}]",
                IncludeCustomFormatWhenRenaming = 0
            });
            InsertProfile(20, "Scored", 22, VrItems, "[{\"format\":8,\"score\":10},{\"format\":9,\"score\":20}]");

            PortQuality();

            V5.Query<int>("SELECT \"Id\" FROM \"CustomFormats\"", transaction: Transaction).Should().Equal(9);

            var formatItems = JsonNode.Parse(V5.ExecuteScalar<string>("SELECT \"FormatItems\" FROM \"QualityProfiles\" WHERE \"Id\" = 20", transaction: Transaction)).AsArray();

            formatItems.Should().HaveCount(1);
            formatItems[0]["format"].GetValue<int>().Should().Be(9);

            ReportFor(LegacyReportCategory.SkippedSetting).Where(r => r.Area == "CustomFormats" && r.Subject == "Azeri").Should().NotBeEmpty();
        }

        [Test]
        public void should_convert_release_profile_indexer_id()
        {
            InsertV2("ReleaseProfiles", new { Id = 3, Required = "[\"a\"]", Ignored = "[\"b\"]", Tags = "[1]", Enabled = 1, IndexerId = 0, Name = "none", ExcludedTags = "[]", AirDateRestriction = 0, AirDateGracePeriod = 0 });
            InsertV2("ReleaseProfiles", new { Id = 4, Required = "[]", Ignored = "[]", Tags = "[]", Enabled = 0, IndexerId = 12, Name = "one", ExcludedTags = "[2]", AirDateRestriction = 1, AirDateGracePeriod = 0 });

            PortQuality();
            Commit();
            Context.Dispose();

            var profiles = Mocker.Resolve<ReleaseProfileRepository>().All().OrderBy(p => p.Id).ToList();

            profiles.Select(p => p.Id).Should().Equal(3, 4);
            profiles[0].IndexerIds.Should().BeEmpty();
            profiles[0].Required.Should().Equal("a");
            profiles[0].Ignored.Should().Equal("b");
            profiles[0].Tags.Should().BeEquivalentTo(new[] { 1 });
            profiles[0].Enabled.Should().BeTrue();
            profiles[1].IndexerIds.Should().Equal(12);
            profiles[1].Enabled.Should().BeFalse();
            profiles[1].Name.Should().Be("one");
        }

        [Test]
        public void should_report_release_profile_excluded_tags_and_air_date_restriction()
        {
            InsertV2("ReleaseProfiles", new { Id = 3, Required = "[]", Ignored = "[]", Tags = "[]", Enabled = 1, IndexerId = 0, Name = "plain", ExcludedTags = "[]", AirDateRestriction = 0, AirDateGracePeriod = 0 });
            InsertV2("ReleaseProfiles", new { Id = 4, Required = "[]", Ignored = "[]", Tags = "[]", Enabled = 1, IndexerId = 0, Name = "excl", ExcludedTags = "[2]", AirDateRestriction = 0, AirDateGracePeriod = 0 });
            InsertV2("ReleaseProfiles", new { Id = 5, Required = "[]", Ignored = "[]", Tags = "[]", Enabled = 1, IndexerId = 0, Name = "air", ExcludedTags = "[]", AirDateRestriction = 1, AirDateGracePeriod = 0 });

            PortQuality();

            ReportFor(LegacyReportCategory.SkippedSetting).Where(r => r.Area == "ReleaseProfiles").Select(r => r.Subject)
                .Should().BeEquivalentTo("excl", "air");
        }

        [Test]
        public void should_drop_network_and_status_auto_tag_specs()
        {
            InsertV2("AutoTagging", new
            {
                Id = 2,
                Name = "mixed",
                Specifications = "[{\"type\":\"NetworkSpecification\",\"body\":{\"name\":\"n\",\"value\":\"x\"}},{\"type\":\"StatusSpecification\",\"body\":{\"name\":\"s\",\"value\":1}},{\"type\":\"OriginalLanguageSpecification\",\"body\":{\"name\":\"o\",\"value\":50}},{\"type\":\"GenreSpecification\",\"body\":{\"name\":\"g\",\"value\":[\"a\"]}}]",
                RemoveTagsAutomatically = 1,
                Tags = "[4]"
            });
            InsertV2("AutoTagging", new
            {
                Id = 3,
                Name = "only network",
                Specifications = "[{\"type\":\"NetworkSpecification\",\"body\":{\"name\":\"n\",\"value\":\"x\"}}]",
                RemoveTagsAutomatically = 0,
                Tags = "[4]"
            });
            InsertV2("AutoTagging", new
            {
                Id = 4,
                Name = "azeri",
                Specifications = "[{\"type\":\"OriginalLanguageSpecification\",\"body\":{\"name\":\"o\",\"value\":47}},{\"type\":\"MonitoredSpecification\",\"body\":{\"name\":\"m\",\"value\":true}}]",
                RemoveTagsAutomatically = 0,
                Tags = "[]"
            });

            PortQuality();
            Commit();
            Context.Dispose();

            var tags = Mocker.Resolve<AutoTaggingRepository>().All().OrderBy(t => t.Id).ToList();

            tags.Select(t => t.Id).Should().Equal(2, 4);
            tags[0].Specifications.Select(s => s.GetType().Name).Should().Equal("OriginalLanguageSpecification", "GenreSpecification");
            tags[0].RemoveTagsAutomatically.Should().BeTrue();
            tags[0].Tags.Should().BeEquivalentTo(new[] { 4 });
            tags[1].Specifications.Select(s => s.GetType().Name).Should().Equal("MonitoredSpecification");
        }

        [Test]
        public void should_report_dropped_auto_tag_specs_and_skipped_rules()
        {
            InsertV2("AutoTagging", new
            {
                Id = 3,
                Name = "only network",
                Specifications = "[{\"type\":\"NetworkSpecification\",\"body\":{\"name\":\"n\",\"value\":\"x\"}}]",
                RemoveTagsAutomatically = 0,
                Tags = "[4]"
            });

            PortQuality();

            V5.ExecuteScalar<int>("SELECT COUNT(*) FROM \"AutoTagging\"", transaction: Transaction).Should().Be(0);
            ReportFor(LegacyReportCategory.SkippedSetting).Where(r => r.Area == "AutoTagging" && r.Subject == "only network").Should().NotBeEmpty();
        }

        [Test]
        public void should_register_inserted_tables()
        {
            PortQuality();

            Context.InsertedTables.Should().Contain(new[] { "QualityProfiles", "CustomFormats", "ReleaseProfiles", "AutoTagging" });
        }
    }
}
