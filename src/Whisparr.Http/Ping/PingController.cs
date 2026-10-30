using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;

namespace Whisparr.Http.Ping
{
    public class PingController : Controller
    {
        private readonly IConfigRepository _configRepository;
        private readonly ICached<IEnumerable<Config>> _cache;
        private readonly ISetupModeState _setupModeState;

        public PingController(IConfigRepository configRepository, ICacheManager cacheManager, ISetupModeState setupModeState)
        {
            _configRepository = configRepository;
            _cache = cacheManager.GetCache<IEnumerable<Config>>(GetType());
            _setupModeState = setupModeState;
        }

        [AllowAnonymous]
        [HttpGet("/ping")]
        [HttpHead("/ping")]
        [Produces("application/json")]
        public ActionResult<PingResource> GetStatus()
        {
            // Setup mode has no v5 database to ask, and a healthcheck shouldn't call a waiting halt screen unhealthy
            if (_setupModeState.IsActive)
            {
                return StatusCode(StatusCodes.Status200OK, new PingResource
                {
                    Status = "OK"
                });
            }

            try
            {
                _cache.Get("ping", _configRepository.All, TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new PingResource
                {
                    Status = "Error"
                });
            }

            return StatusCode(StatusCodes.Status200OK, new PingResource
            {
                Status = "OK"
            });
        }
    }
}
