using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;

namespace NzbDrone.Core.RootFolders
{
    public interface IImportFileMatchService
    {
        // Looks up hashed import files that have no stored result yet and stores the candidates.
        void MatchPending();
    }

    public class ImportFileMatchService : IImportFileMatchService
    {
        // Upper bound on files looked up per run; the next run takes the rest
        internal const int MaxFilesPerRun = 500;

        // Upper bound on remote scene-info calls per run, so a big backlog can't hammer the metadata service
        internal const int MaxEnrichmentsPerRun = 100;

        private readonly IImportlFileService _importFileService;
        private readonly IFingerprintLookupClient _lookupClient;
        private readonly IFileMatchEvaluator _matchEvaluator;
        private readonly Logger _logger;

        private int _watermark;

        public ImportFileMatchService(IImportlFileService importFileService,
                                      IFingerprintLookupClient lookupClient,
                                      IFileMatchEvaluator matchEvaluator,
                                      Logger logger)
        {
            UtcNow = () => DateTime.UtcNow;
            _importFileService = importFileService;
            _lookupClient = lookupClient;
            _matchEvaluator = matchEvaluator;
            _logger = logger;
        }

        // Replaceable so tests can pin the stamp
        internal Func<DateTime> UtcNow { get; set; }

        public void MatchPending()
        {
            var pending = _importFileService.GetFilesNeedingCandidates(_watermark, MaxFilesPerRun);

            // Rows that stay TryLater stay unstamped; the watermark moves past them so they can't
            // block later rows, and wraps at the end of the table so they are retried once per pass
            _watermark = pending.Count < MaxFilesPerRun ? 0 : pending[^1].Id;

            if (pending.Count == 0)
            {
                return;
            }

            // Import files weren't matched by the parser, so the lookup goes upstream
            var fingerprints = pending
                .Select(f => new FileFingerprint { Key = f.Id.ToString(), OsHash = f.OsHash, PHash = f.PHash })
                .ToList();

            var results = _lookupClient.Lookup(fingerprints, false);
            var stamp = UtcNow();
            var done = new List<ImportFile>();

            var enrichBudget = MaxEnrichmentsPerRun;

            foreach (var file in pending)
            {
                // A missing entry or TryLater leaves the row unstamped for the next run
                if (!results.TryGetValue(file.Id.ToString(), out var result) || result.Status == FingerprintLookupStatus.TryLater)
                {
                    continue;
                }

                if (result.Status == FingerprintLookupStatus.Matched)
                {
                    // Candidates past the run's budget keep library info only; a later run won't revisit them
                    file.Candidates = _matchEvaluator.BuildCandidates(result, false);

                    if (enrichBudget > 0)
                    {
                        enrichBudget -= _matchEvaluator.EnrichCandidates(file.Candidates, enrichBudget);
                    }
                }
                else
                {
                    file.Candidates = new List<FingerprintCandidate>();
                }

                file.CandidatesCheckedAt = stamp;
                done.Add(file);
            }

            if (done.Count > 0)
            {
                _importFileService.SaveCandidates(done);
            }

            _logger.Debug("Stored fingerprint candidates for {0} of {1} import files", done.Count, pending.Count);
        }
    }
}
