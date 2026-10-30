using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.LegacyMigration;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class LegacySceneMatcherFixture
    {
        private static readonly DateTime Day = new DateTime(2024, 3, 5);

        [Test]
        public void exact_title_and_date_matches()
        {
            var catalog = new[] { new SceneKey("a", "Scene One", Day), new SceneKey("b", "Other", Day) };

            LegacySceneMatcher.FindExact(catalog, "Scene One", Day).Should().Be("a");
        }

        [Test]
        public void title_differs_only_in_punctuation_matches()
        {
            var catalog = new[] { new SceneKey("a", "Scene One", Day) };

            LegacySceneMatcher.FindExact(catalog, "Scene: One!", Day.AddHours(5)).Should().Be("a");
        }

        [Test]
        public void same_title_other_day_is_null()
        {
            var catalog = new[] { new SceneKey("a", "Scene One", Day) };

            LegacySceneMatcher.FindExact(catalog, "Scene One", Day.AddDays(1)).Should().BeNull();
        }

        [Test]
        public void duplicate_catalog_entries_same_id_match()
        {
            var catalog = new[] { new SceneKey("a", "Scene One", Day), new SceneKey("a", "Scene One", Day) };

            LegacySceneMatcher.FindExact(catalog, "Scene One", Day).Should().Be("a");
        }

        [Test]
        public void two_ids_is_null()
        {
            var catalog = new[] { new SceneKey("a", "Scene One", Day), new SceneKey("b", "Scene One", Day) };

            LegacySceneMatcher.FindExact(catalog, "Scene One", Day).Should().BeNull();
        }

        [Test]
        public void null_date_is_null()
        {
            var catalog = new[] { new SceneKey("a", "Scene One", Day) };

            LegacySceneMatcher.FindExact(catalog, "Scene One", null).Should().BeNull();
        }

        [Test]
        public void blank_title_is_null()
        {
            var catalog = new[] { new SceneKey("a", "Scene One", Day) };

            LegacySceneMatcher.FindExact(catalog, " ", Day).Should().BeNull();
        }

        [TestCase("!!!")]
        [TestCase("?!")]
        [TestCase("... ---")]
        public void a_title_that_cleans_to_nothing_never_matches(string title)
        {
            var catalog = new[] { new SceneKey("a", title, Day), new SceneKey("b", "Scene One", Day) };

            LegacySceneMatcher.FindExact(catalog, title, Day).Should().BeNull();
        }

        [Test]
        public void catalog_entries_that_clean_to_nothing_are_never_matched()
        {
            var catalog = new[] { new SceneKey("a", "...", Day), new SceneKey("b", "Scene One", Day) };

            LegacySceneMatcher.FindExact(catalog, "Scene One", Day).Should().Be("b");
        }
    }
}
