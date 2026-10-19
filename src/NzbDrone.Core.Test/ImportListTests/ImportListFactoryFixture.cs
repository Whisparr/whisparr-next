using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.RSSImport;
using NzbDrone.Core.ImportLists.StashDB.Favorite;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    [TestFixture]
    public class ImportListFactoryFixture : CoreTest<ImportListFactory>
    {
        [SetUp]
        public void Setup()
        {
            var stashDbList = Mocker.Resolve<StashDBFavoriteImport>();
            var rssList = Mocker.Resolve<RSSImport>();

            Mocker.SetConstant<IEnumerable<IImportList>>(new IImportList[] { stashDbList, rssList });

            Mocker.GetMock<IServiceProvider>()
                  .Setup(s => s.GetService(typeof(StashDBFavoriteImport)))
                  .Returns(stashDbList);
            Mocker.GetMock<IServiceProvider>()
                  .Setup(s => s.GetService(typeof(RSSImport)))
                  .Returns(rssList);

            Mocker.GetMock<IImportListStatusService>()
                  .Setup(s => s.GetBlockedProviders())
                  .Returns(new List<ImportListStatus>());

            Mocker.GetMock<IImportListRepository>()
                  .Setup(s => s.All())
                  .Returns(new List<ImportListDefinition>
                  {
                      new()
                      {
                          Id = 1,
                          Name = "StashDB favourites",
                          Enabled = true,
                          Implementation = nameof(StashDBFavoriteImport),
                          Settings = new StashDBFavoriteSettings { ApiKey = "key" }
                      },
                      new()
                      {
                          Id = 2,
                          Name = "RSS",
                          Enabled = true,
                          Implementation = nameof(RSSImport),
                          Settings = new RSSImportSettings()
                      }
                  });
        }

        private void GivenSceneSource(SceneMetadataType source)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.WhisparrSceneMetadataSource)
                  .Returns(source);
        }

        [Test]
        public void should_hide_stashdb_lists_when_source_is_tpdb()
        {
            GivenSceneSource(SceneMetadataType.Tpdb);

            Subject.Enabled().Select(l => l.GetType()).Should().Equal(typeof(RSSImport));
            Subject.GetAvailableProviders().Select(l => l.GetType()).Should().Equal(typeof(RSSImport));
            Subject.GetDefaultDefinitions().Select(d => d.Implementation).Should().Equal(nameof(RSSImport));
        }

        [Test]
        public void should_offer_stashdb_lists_when_source_is_stashdb()
        {
            GivenSceneSource(SceneMetadataType.StashDb);

            Subject.Enabled().Select(l => l.GetType()).Should().Equal(typeof(StashDBFavoriteImport), typeof(RSSImport));
            Subject.GetDefaultDefinitions().Select(d => d.Implementation).Should().Equal(nameof(StashDBFavoriteImport), nameof(RSSImport));
        }

        [Test]
        public void should_keep_stashdb_lists_in_all_when_source_is_tpdb()
        {
            GivenSceneSource(SceneMetadataType.Tpdb);

            Subject.All().Should().HaveCount(2);
        }
    }
}
