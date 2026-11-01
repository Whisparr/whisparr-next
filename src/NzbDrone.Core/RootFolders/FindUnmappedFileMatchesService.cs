using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.Core.RootFolders.Commands;

namespace NzbDrone.Core.RootFolders
{
    // Runs the modal's search for the files the user asked for and stores the top result on each, one file at a time.
    public class FindUnmappedFileMatchesService : IExecute<FindUnmappedFileMatchesCommand>
    {
        private const int MaxLeadingFailures = 3;

        private readonly IImportFileRepository _importFileRepository;
        private readonly IUnmappedFileService _unmappedFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly IReportCommandProgress _progressReporter;
        private readonly IEventAggregator _eventAggregator;
        private readonly ILocalizationService _localizationService;
        private readonly Logger _logger;

        public FindUnmappedFileMatchesService(IImportFileRepository importFileRepository,
                                              IUnmappedFileService unmappedFileService,
                                              IDiskProvider diskProvider,
                                              IReportCommandProgress progressReporter,
                                              IEventAggregator eventAggregator,
                                              ILocalizationService localizationService,
                                              Logger logger)
        {
            _importFileRepository = importFileRepository;
            _unmappedFileService = unmappedFileService;
            _diskProvider = diskProvider;
            _progressReporter = progressReporter;
            _eventAggregator = eventAggregator;
            _localizationService = localizationService;
            _logger = logger;
        }

        public void Execute(FindUnmappedFileMatchesCommand message)
        {
            var ids = (message.FileIds ?? new List<int>()).Distinct().ToList();

            if (ids.Count == 0)
            {
                return;
            }

            var files = _importFileRepository.FindByIds(ids).ToDictionary(f => f.Id);
            var done = 0;
            var checkedCount = 0;
            var matched = 0;
            var failed = 0;
            var succeeded = false;

            foreach (var id in ids)
            {
                var outcome = files.TryGetValue(id, out var file) && !file.Dismissed && _diskProvider.FileExists(file.Path)
                    ? FindMatch(file)
                    : Outcome.Skipped;

                if (outcome == Outcome.Checked)
                {
                    succeeded = true;
                    checkedCount++;

                    if (file.TopMatch != null && file.TopMatch.IsStrong())
                    {
                        matched++;
                    }
                }
                else if (outcome == Outcome.Failed)
                {
                    failed++;

                    // An outage would otherwise walk the whole page
                    StopIfOutage(failed, succeeded);
                }

                done++;
                _progressReporter.Report("Finding matches", done, ids.Count, done * 100.0 / ids.Count);
            }

            message.SetCompletionMessage(_localizationService.GetLocalizedString("FindUnmappedFileMatchesComplete", new Dictionary<string, object>
            {
                { "checked", checkedCount },
                { "matched", matched },
                { "failed", failed }
            }));
        }

        private void StopIfOutage(int failed, bool succeeded)
        {
            if (!succeeded && failed >= MaxLeadingFailures)
            {
                throw new CommandFailedException(_localizationService.GetLocalizedString("FindUnmappedFileMatchesStopped", new Dictionary<string, object> { { "count", failed } }));
            }
        }

        private Outcome FindMatch(ImportFile file)
        {
            UnmappedFileMatch found;

            try
            {
                var top = _unmappedFileService.Suggest(file.Id, null, ItemType.Scene).Results?.FirstOrDefault();

                found = top == null ? null : ToMatch(top);
            }
            catch (ModelNotFoundException)
            {
                // Removed since it was loaded, so there is nothing to check; not a failed search
                return Outcome.Skipped;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to find matches for {0}", file.Path);

                return Outcome.Failed;
            }

            file.TopMatch = found;
            file.TopMatchCheckedAt = DateTime.UtcNow;

            _importFileRepository.SaveTopMatch(new List<ImportFile> { file });
            _eventAggregator.PublishEvent(new UnmappedFileMatchCheckedEvent(file));

            return Outcome.Checked;
        }

        private enum Outcome
        {
            Checked,
            Skipped,
            Failed
        }

        private static UnmappedFileMatch ToMatch(UnmappedFileSuggestion suggestion)
        {
            return new UnmappedFileMatch
            {
                ForeignId = suggestion.Movie.ForeignId,
                Title = suggestion.Movie.Title,
                StudioTitle = suggestion.Movie.MovieMetadata.Value.StudioTitle,
                ReleaseDate = suggestion.Movie.GetReleaseDate(),
                HashMatch = suggestion.HashMatch,
                SameDate = suggestion.SameDate,
                SameStudio = suggestion.SameStudio
            };
        }
    }
}
