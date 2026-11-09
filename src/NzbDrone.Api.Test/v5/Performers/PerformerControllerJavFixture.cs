using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.Performers;
using Whisparr.Http.REST;

namespace NzbDrone.Api.Test.v5.Performers
{
    [TestFixture]
    public class PerformerControllerJavFixture : TestBase<PerformerController>
    {
        private const string JavMessage = "JavRequiresTpdbSceneSource";

        private void GivenJavAvailable(bool available)
        {
            Mocker.GetMock<IJavAvailability>().Setup(s => s.IsAvailable()).Returns(available);
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(JavMessage))
                  .Returns(JavMessage);
        }

        private List<string> SharedErrors(PerformerResource resource)
        {
            var validator = (ResourceValidator<PerformerResource>)typeof(RestController<PerformerResource>)
                .GetProperty("SharedValidator", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Subject);

            return validator.Validate(resource).Errors.Select(e => e.ErrorMessage).ToList();
        }

        private static PerformerResource Resource(bool javMonitored)
        {
            return new PerformerResource { Id = 1, ForeignId = "p", Name = "P", JavMonitored = javMonitored };
        }

        [Test]
        public void performer_put_jav_monitored_rejected_under_stashdb()
        {
            GivenJavAvailable(false);

            SharedErrors(Resource(true)).Should().Contain(JavMessage);
        }

        [Test]
        public void performer_put_jav_monitored_accepted_when_tpdb_available()
        {
            GivenJavAvailable(true);

            SharedErrors(Resource(true)).Should().NotContain(JavMessage);
        }

        [Test]
        public void performer_put_jav_unmonitored_is_accepted_under_stashdb()
        {
            GivenJavAvailable(false);

            SharedErrors(Resource(false)).Should().NotContain(JavMessage);
        }
    }
}
