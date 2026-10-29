using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.LegacyMigration;
using Whisparr.Http;

namespace Whisparr.Api.V5.System.MigrationReport
{
    [V5ApiController("system/migrationreport")]
    public class MigrationReportController : Controller
    {
        private readonly ILegacyMigrationService _migrationService;

        public MigrationReportController(ILegacyMigrationService migrationService)
        {
            _migrationService = migrationService;
        }

        [HttpGet]
        public ActionResult<MigrationReportResource> GetReport()
        {
            var state = _migrationService.GetCurrent();

            if (state == null)
            {
                return NotFound();
            }

            var items = _migrationService.GetReport()
                .OrderBy(i => i.Category)
                .ThenBy(i => i.Area)
                .ThenBy(i => i.Subject)
                .Select(i => new MigrationReportItemResource
                {
                    Category = i.Category.ToString(),
                    Area = i.Area,
                    Subject = i.Subject,
                    Detail = i.Detail,
                    Count = i.Count
                })
                .ToList();

            return new MigrationReportResource
            {
                SourceKind = state.SourceKind.ToString(),
                SceneSource = state.SceneSource.ToString(),
                MovieSource = state.MovieSource.ToString(),
                Completed = state.Completed,
                StartedAt = state.StartedAt,
                CompletedAt = state.CompletedAt,
                BackupPath = state.BackupPath,
                Dismissed = state.ReportDismissed,
                Items = items
            };
        }

        [HttpPost("dismiss")]
        public IActionResult Dismiss()
        {
            if (_migrationService.GetCurrent() == null)
            {
                return NotFound();
            }

            _migrationService.DismissReport();

            return Ok();
        }
    }
}
