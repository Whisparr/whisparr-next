using System.Collections.Generic;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Performers;

namespace NzbDrone.Api.Test.v5.Performers
{
    [TestFixture]
    public class PerformerEditorControllerJavFixture : TestBase<PerformerEditorController>
    {
        private const string JavMessage = "JavRequiresTpdbSceneSource";

        private Performer _performer;

        [SetUp]
        public void Setup()
        {
            _performer = new Performer { Id = 1, ForeignId = "p", Name = "P", JavMonitored = false, QualityProfileId = 1 };

            Mocker.GetMock<IPerformerService>()
                  .Setup(s => s.GetPerformers(It.IsAny<List<int>>()))
                  .Returns(new List<Performer> { _performer });
            Mocker.GetMock<IPerformerService>()
                  .Setup(s => s.Update(It.IsAny<List<Performer>>()))
                  .Returns<List<Performer>>(p => p);
            Mocker.GetMock<IQualityProfileService>().Setup(s => s.Exists(1)).Returns(true);
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(JavMessage))
                  .Returns(JavMessage);
        }

        private void GivenJavAvailable(bool available)
        {
            var javAvailability = new Mock<IJavAvailability>();
            javAvailability.Setup(s => s.IsAvailable()).Returns(available);

            // The real validator, so the availability rule actually runs.
            Mocker.SetConstant(new PerformerEditorValidator(
                new RootFolderExistsValidator<Performer>(Mocker.Resolve<IRootFolderService>()),
                new QualityProfileExistsValidator<Performer>(Mocker.Resolve<IQualityProfileService>()),
                javAvailability.Object,
                Mocker.Resolve<ILocalizationService>()));
        }

        [Test]
        public void editor_rejects_jav_monitored_when_jav_unavailable()
        {
            GivenJavAvailable(false);

            var save = () => Subject.SaveAll(new PerformerEditorResource { PerformerIds = new List<int> { 1 }, JavMonitored = true });

            save.Should().Throw<ValidationException>().Which.Errors.Should().Contain(e => e.ErrorMessage == JavMessage);
            Mocker.GetMock<IPerformerService>().Verify(s => s.Update(It.IsAny<List<Performer>>()), Times.Never());
        }

        [Test]
        public void editor_accepts_jav_monitored_when_jav_available()
        {
            GivenJavAvailable(true);

            Subject.SaveAll(new PerformerEditorResource { PerformerIds = new List<int> { 1 }, JavMonitored = true });

            _performer.JavMonitored.Should().BeTrue();
        }

        [Test]
        public void editor_leaves_jav_monitored_untouched_when_null()
        {
            GivenJavAvailable(true);
            _performer.JavMonitored = true;

            Subject.SaveAll(new PerformerEditorResource { PerformerIds = new List<int> { 1 }, Monitored = true, JavMonitored = null });

            _performer.JavMonitored.Should().BeTrue();
        }
    }
}
