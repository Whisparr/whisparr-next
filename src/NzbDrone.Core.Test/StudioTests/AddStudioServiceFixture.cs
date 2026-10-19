using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.StudioTests
{
    [TestFixture]
    public class AddStudioServiceFixture : CoreTest<AddStudioService>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.AllStudioForeignIds())
                  .Returns(new List<string>());

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetStudioInfo(It.IsAny<string>()))
                  .Returns<string>(id => new Studio { ForeignId = id, Title = "Studio " + id });
        }

        [Test]
        public void should_lock_metadata_sources_once_when_adding_a_studio()
        {
            Subject.AddStudio(new Studio { ForeignId = "studio-1" });

            Mocker.GetMock<IMetadataSourceService>().Verify(v => v.EnsureLocked(), Times.Once());
        }

        [Test]
        public void should_lock_metadata_sources_once_per_batch()
        {
            Subject.AddStudios(new List<Studio>
            {
                new Studio { ForeignId = "studio-1" },
                new Studio { ForeignId = "studio-2" }
            });

            Mocker.GetMock<IMetadataSourceService>().Verify(v => v.EnsureLocked(), Times.Once());
        }
    }
}
