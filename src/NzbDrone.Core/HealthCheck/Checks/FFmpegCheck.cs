using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.Fingerprints;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(ConfigSavedEvent))]
    public class FFmpegCheck : HealthCheckBase
    {
        private readonly IConfigService _configService;
        private readonly IVideoHashService _videoHashService;

        public FFmpegCheck(IConfigService configService, IVideoHashService videoHashService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _configService = configService;
            _videoHashService = videoHashService;
        }

        public override HealthCheck Check()
        {
            if (_configService.EnablePerceptualHashing && !_videoHashService.CanComputePHash)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.FFmpegUnavailable,
                    _localizationService.GetLocalizedString("FFmpegUnavailableHealthCheckMessage"),
                    "#ffmpeg-unavailable");
            }

            return new HealthCheck(GetType());
        }
    }
}
