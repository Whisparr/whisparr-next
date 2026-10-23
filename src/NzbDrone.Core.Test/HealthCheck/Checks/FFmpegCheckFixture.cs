using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.HealthCheck.Checks
{
    [TestFixture]
    public class FFmpegCheckFixture : CoreTest<FFmpegCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString("FFmpegUnavailableHealthCheckMessage"))
                  .Returns("The bundled ffmpeg needed for perceptual hashing was not found or can't run.");
        }

        private void GivenPerceptualHashing(bool enabled)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.EnablePerceptualHashing)
                  .Returns(enabled);
        }

        private void GivenFFmpegAvailable(bool available)
        {
            Mocker.GetMock<IVideoHashService>()
                  .SetupGet(s => s.CanComputePHash)
                  .Returns(available);
        }

        [Test]
        public void should_return_ok_when_perceptual_hashing_is_off_and_ffmpeg_is_unavailable()
        {
            GivenPerceptualHashing(false);
            GivenFFmpegAvailable(false);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_ok_when_perceptual_hashing_is_off_and_ffmpeg_is_available()
        {
            GivenPerceptualHashing(false);
            GivenFFmpegAvailable(true);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_ok_when_perceptual_hashing_is_on_and_ffmpeg_is_available()
        {
            GivenPerceptualHashing(true);
            GivenFFmpegAvailable(true);

            Subject.Check().ShouldBeOk();
        }

        [Test]
        public void should_return_warning_when_perceptual_hashing_is_on_and_ffmpeg_is_unavailable()
        {
            GivenPerceptualHashing(true);
            GivenFFmpegAvailable(false);

            Subject.Check().ShouldBeWarning(
                "The bundled ffmpeg needed for perceptual hashing was not found or can't run.",
                "#ffmpeg-unavailable",
                HealthCheckReason.FFmpegUnavailable);
        }
    }
}
