using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Extras.Subtitles
{
    public class ExistingSubtitleImporter : ImportExistingExtraFilesBase<SubtitleFile>
    {
        private readonly IExtraFileService<SubtitleFile> _subtitleFileService;
        private readonly IParsingService _parsingService;
        private readonly ISceneFolderLayout _sceneFolderLayout;
        private readonly ISidecarOwnership _sidecarOwnership;
        private readonly Logger _logger;

        public ExistingSubtitleImporter(IExtraFileService<SubtitleFile> subtitleFileService,
                                        IParsingService parsingService,
                                        ISceneFolderLayout sceneFolderLayout,
                                        ISidecarOwnership sidecarOwnership,
                                        IMediaFileService mediaFileService,
                                        Logger logger)
            : base(subtitleFileService, mediaFileService)
        {
            _subtitleFileService = subtitleFileService;
            _parsingService = parsingService;
            _sceneFolderLayout = sceneFolderLayout;
            _sidecarOwnership = sidecarOwnership;
            _logger = logger;
        }

        public override int Order => 1;

        public override IEnumerable<ExtraFile> ProcessFiles(Movie movie, List<string> filesOnDisk, List<string> importedFiles, string fileNameBeforeRename)
        {
            _logger.Debug("Looking for existing subtitle files in {0}", movie.Path);

            var subtitleFiles = new List<SubtitleFile>();
            var filterResult = FilterAndClean(movie, filesOnDisk, importedFiles, fileNameBeforeRename is not null);

            var shared = _sceneFolderLayout.IsShared(movie);
            var folderFiles = shared ? _sidecarOwnership.GetFolderFiles(movie) : null;

            foreach (var possibleSubtitleFile in filterResult.FilesOnDisk)
            {
                var extension = Path.GetExtension(possibleSubtitleFile);

                if (SubtitleFileExtensions.Extensions.Contains(extension))
                {
                    var movieFileId = shared ? 0 : GetCurrentMovieFileId(movie);

                    if (shared)
                    {
                        var owner = _sidecarOwnership.FindOwner(movie, possibleSubtitleFile, folderFiles);

                        if (owner == null)
                        {
                            _logger.Debug("Subtitle file: {0} is not owned by {1}", possibleSubtitleFile, movie);
                            continue;
                        }

                        movieFileId = owner.Id;
                    }

                    var minimalInfo = _parsingService.ParseMinimalPathMovieInfo(possibleSubtitleFile);

                    if (minimalInfo == null)
                    {
                        _logger.Debug("Unable to parse subtitle file: {0}", possibleSubtitleFile);
                        continue;
                    }

                    var subtitleFile = new SubtitleFile
                    {
                        MovieId = movie.Id,
                        MovieFileId = movieFileId,
                        RelativePath = movie.Path.GetRelativePath(possibleSubtitleFile),
                        Language = LanguageParser.ParseSubtitleLanguage(possibleSubtitleFile),
                        LanguageTags = LanguageParser.ParseLanguageTags(possibleSubtitleFile),
                        Extension = extension
                    };

                    subtitleFiles.Add(subtitleFile);
                }
            }

            _logger.Trace("Found {0} existing subtitle files", subtitleFiles.Count);
            _subtitleFileService.Upsert(subtitleFiles);

            // Return files that were just imported along with files that were
            // previously imported so previously imported files aren't imported twice
            return subtitleFiles.Concat(filterResult.PreviouslyImported);
        }
    }
}
