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
                    Id = i.Id,
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
            var state = _migrationService.GetCurrent();

            if (state == null)
            {
                return NotFound();
            }

            // The report is still filling in until the import completes, so it can't be put away yet
            if (!state.Completed)
            {
                return Conflict();
            }

            _migrationService.DismissReport();

            return Ok();
        }

        [HttpDelete("item/{id:int}")]
        public IActionResult RemoveItem(int id)
        {
            var state = _migrationService.GetCurrent();

            if (state == null)
            {
                return NotFound();
            }

            // The report is still filling in until the import completes, so it can't be edited yet
            if (!state.Completed)
            {
                return Conflict();
            }

            _migrationService.RemoveReportItem(id);

            return Ok();
        }
    }
}
