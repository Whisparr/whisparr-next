using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;

namespace Whisparr.Http.Setup
{
    // The migration wizard's API. Only exists while a legacy install is waiting to be migrated.
    [ApiController]
    [Route("api/setup")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class SetupController : ControllerBase
    {
        private readonly ISetupModeState _setupModeState;
        private readonly ISetupWizardService _wizard;
        private readonly IConfig5Seeder _config5Seeder;

        public SetupController(ISetupModeState setupModeState, ISetupWizardService wizard, IConfig5Seeder config5Seeder)
        {
            _setupModeState = setupModeState;
            _wizard = wizard;
            _config5Seeder = config5Seeder;
        }

        [HttpGet("status")]
        public ActionResult<SetupStatusResource> GetStatus()
        {
            if (!_setupModeState.IsActive)
            {
                return NotFound();
            }

            var detection = _setupModeState.Detection;
            var envTargets = detection.EnvTargets;

            if (envTargets.Count == 0)
            {
                envTargets = PlanEnvTargets(detection);
            }

            return new SetupStatusResource
            {
                Installs = detection.Installs.Select(SetupInstallResource.MapToResource).ToList(),
                Errors = detection.Errors,
                PendingEnvSwitch = detection.PendingEnvSwitch,
                EnvTargets = envTargets.Select(SetupEnvTargetResource.MapToResource).ToList(),
                Defaults = GetDefaults(detection),
                Progress = SetupProgressResource.MapToResource(_wizard.Progress)
            };
        }

        [HttpPost("migrate")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public IActionResult Migrate([FromBody] SetupChoiceResource choice)
        {
            if (!_setupModeState.IsActive)
            {
                return NotFound();
            }

            var detection = _setupModeState.Detection;
            var install = detection.Installs.FirstOrDefault(i => i.Kind == choice.Kind);

            if (install == null)
            {
                return Conflict(new { message = $"No {choice.Kind} install was found to migrate." });
            }

            if (install.Refusal != LegacyRefusal.None)
            {
                return Conflict(new { message = $"The {choice.Kind} install can't be migrated: {install.Refusal}." });
            }

            if (detection.PendingEnvSwitch)
            {
                return Conflict(new { message = "The migration is done. Change the environment variables first." });
            }

            var status = _wizard.Progress.Status;

            if (status != SetupStatus.Idle && status != SetupStatus.Failed)
            {
                return Conflict(new { message = "The migration is already running." });
            }

            _wizard.Start(choice.ToModel());

            return Accepted();
        }

        [HttpPost("retry")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public IActionResult Retry()
        {
            if (!_setupModeState.IsActive)
            {
                return NotFound();
            }

            _wizard.Retry();

            return Accepted();
        }

        [HttpPost("shutdown")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public IActionResult ShutDown()
        {
            if (!_setupModeState.IsActive)
            {
                return NotFound();
            }

            _wizard.ShutDown();

            return Accepted();
        }

        // What the page preselects: the only install, or Eros when both exist.
        private static SetupDefaultsResource GetDefaults(DetectionResult detection)
        {
            var eros = detection.Installs.FirstOrDefault(i => i.Kind == LegacyKind.Eros);
            var v2 = detection.Installs.FirstOrDefault(i => i.Kind == LegacyKind.V2);

            if (eros == null && v2 != null)
            {
                return new SetupDefaultsResource
                {
                    Kind = LegacyKind.V2,
                    SceneSource = SceneMetadataType.Tpdb,
                    MovieSource = MovieMetadataType.TPDB
                };
            }

            return new SetupDefaultsResource
            {
                Kind = LegacyKind.Eros,
                SceneSource = SceneMetadataType.StashDb,
                MovieSource = eros == null || eros.PreviousMovieSource == MovieMetadataType.NONE ? MovieMetadataType.TMDB : eros.PreviousMovieSource
            };
        }

        // Before migrating nothing has been seeded yet, so show the values the seeder will pick.
        private List<EnvTarget> PlanEnvTargets(DetectionResult detection)
        {
            var named = detection.Installs.Where(i => i.Database?.NamedByEnvironment == true).ToList();
            var defaultKind = GetDefaults(detection).Kind;
            var install = named.FirstOrDefault(i => i.Kind == defaultKind) ?? named.FirstOrDefault();

            if (install == null || string.IsNullOrEmpty(detection.LegacyConfigPath))
            {
                return new();
            }

            try
            {
                return _config5Seeder.PlanTargets(detection.LegacyConfigPath, install)?.EnvTargets ?? new();
            }
            catch (InvalidConfigFileException)
            {
                return new();
            }
        }
    }
}
