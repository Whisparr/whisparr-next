using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.StudioTests
{
    [TestFixture]
    public class StudioRepositoryFixture : DbTest<StudioRepository, Studio>
    {
        private Studio GivenStudio(string title, string cleanSearchTitle, params string[] aliases)
        {
            var studio = new Studio
            {
                ForeignId = title.ToLowerInvariant() + "-id",
                Title = title,
                CleanTitle = title.ToLowerInvariant(),
                RootFolderPath = "/movies",
                CleanSearchTitle = cleanSearchTitle,
                Aliases = aliases.ToList()
            };

            return Subject.Insert(studio);
        }

        [Test]
        public void should_map_clean_title_search_title_and_aliases_to_the_studio()
        {
            var studio = GivenStudio("Cosmid", "cosmidsearch", "Cosmid Studios");

            var titles = Subject.AllCleanTitles();

            titles["cosmid"].Should().Equal(studio.Id);
            titles["cosmidsearch"].Should().Equal(studio.Id);
            titles["cosmidstudios"].Should().Equal(studio.Id);
        }

        [Test]
        public void should_map_a_shared_title_to_every_studio()
        {
            var first = GivenStudio("Cosmid", null);
            var second = GivenStudio("Other", null, "Cosmid");

            Subject.AllCleanTitles()["cosmid"].Should().BeEquivalentTo(new[] { first.Id, second.Id });
        }

        [Test]
        public void should_list_a_studio_once_per_title()
        {
            var studio = GivenStudio("Cosmid", "cosmid", "Cosmid");

            Subject.AllCleanTitles()["cosmid"].Should().Equal(studio.Id);
        }

        [Test]
        public void should_not_map_blank_titles()
        {
            // CleanSearchTitle is written as string.Empty by the API's resource mapper when a
            // studio has no search title, and left null by the metadata and scan paths. A
            // blank key used to match every studio saved through the UI.
            GivenStudio("Cosmid", string.Empty, " ");
            GivenStudio("Transfixed", null);

            Subject.AllCleanTitles().Keys.Should().BeEquivalentTo("cosmid", "transfixed");
        }

        [Test]
        public void should_skip_ids_that_no_longer_exist()
        {
            var studio = GivenStudio("Cosmid", null);

            Subject.FindByIds(new List<int> { studio.Id, studio.Id + 100 }).Should().ContainSingle(s => s.Id == studio.Id);
        }
    }
}
