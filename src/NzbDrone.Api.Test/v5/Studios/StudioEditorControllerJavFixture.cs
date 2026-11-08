using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Studios;

namespace NzbDrone.Api.Test.v5.Studios
{
    [TestFixture]
    public class StudioEditorControllerJavFixture : TestBase<StudioEditorController>
    {
        private Studio _studio;

        [SetUp]
        public void Setup()
        {
            _studio = new Studio { Id = 1, ForeignId = "s", Title = "S", JavMonitored = true, QualityProfileId = 1 };

            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.GetStudios(It.IsAny<List<int>>()))
                  .Returns(new List<Studio> { _studio });
            Mocker.GetMock<IStudioService>()
                  .Setup(s => s.Update(It.IsAny<List<Studio>>()))
                  .Returns<List<Studio>>(s => s);
            Mocker.GetMock<IJavAvailability>().Setup(s => s.IsAvailable()).Returns(true);

            Mocker.GetMock<IQualityProfileService>().Setup(s => s.Exists(1)).Returns(true);
        }

        [Test]
        public void editor_leaves_jav_monitored_untouched_when_null()
        {
            Subject.SaveAll(new StudioEditorResource { StudioIds = new List<int> { 1 }, Monitored = true, JavMonitored = null });

            _studio.JavMonitored.Should().BeTrue();
        }

        [Test]
        public void editor_sets_jav_monitored_when_provided()
        {
            Subject.SaveAll(new StudioEditorResource { StudioIds = new List<int> { 1 }, JavMonitored = false });

            _studio.JavMonitored.Should().BeFalse();
        }
    }
}
