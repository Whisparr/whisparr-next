using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Extras.Others
{
    public class ExistingOtherExtraImporter : ImportExistingExtraFilesBase<OtherExtraFile>
    {
        private readonly IExtraFileService<OtherExtraFile> _otherExtraFileService;
        private readonly IParsingService _parsingService;
        private readonly ISceneFolderLayout _sceneFolderLayout;
        private readonly ISidecarOwnership _sidecarOwnership;
        private readonly Logger _logger;

        public ExistingOtherExtraImporter(IExtraFileService<OtherExtraFile> otherExtraFileService,
                                          IParsingService parsingService,
                                          ISceneFolderLayout sceneFolderLayout,
                                          ISidecarOwnership sidecarOwnership,
                                          Logger logger)
            : base(otherExtraFileService)
        {
            _otherExtraFileService = otherExtraFileService;
            _parsingService = parsingService;
            _sceneFolderLayout = sceneFolderLayout;
            _sidecarOwnership = sidecarOwnership;
            _logger = logger;
        }

        public override int Order => 2;

        public override IEnumerable<ExtraFile> ProcessFiles(Movie movie, List<string> filesOnDisk, List<string> importedFiles, string fileNameBeforeRename)
        {
            _logger.Debug("Looking for existing extra files in {0}", movie.Path);

            var extraFiles = new List<OtherExtraFile>();
            var filterResult = FilterAndClean(movie, filesOnDisk, importedFiles, fileNameBeforeRename is not null);

            var shared = _sceneFolderLayout.IsShared(movie);
            var folderFiles = shared ? _sidecarOwnership.GetFolderFiles(movie) : null;

            foreach (var possibleExtraFile in filterResult.FilesOnDisk)
            {
                var extension = Path.GetExtension(possibleExtraFile);

                if (extension.IsNullOrWhiteSpace())
                {
                    _logger.Debug("No extension for file: {0}", possibleExtraFile);
                    continue;
                }

                var movieFileId = movie.MovieFileId;

                if (shared)
                {
                    var owner = _sidecarOwnership.FindOwner(movie, possibleExtraFile, folderFiles);

                    if (owner == null)
                    {
                        _logger.Debug("Extra file: {0} is not owned by {1}", possibleExtraFile, movie);
                        continue;
                    }

                    movieFileId = owner.Id;
                }

                var minimalInfo = _parsingService.ParseMinimalPathMovieInfo(possibleExtraFile);

                if (minimalInfo == null)
                {
                    _logger.Debug("Unable to parse extra file: {0}", possibleExtraFile);
                    continue;
                }

                var extraFile = new OtherExtraFile
                {
                    MovieId = movie.Id,
                    MovieFileId = movieFileId,
                    RelativePath = movie.Path.GetRelativePath(possibleExtraFile),
                    Extension = extension
                };

                extraFiles.Add(extraFile);
            }

            _logger.Trace("Found {0} existing other extra files", extraFiles.Count);
            _otherExtraFileService.Upsert(extraFiles);

            // Return files that were just imported along with files that were
            // previously imported so previously imported files aren't imported twice
            return extraFiles.Concat(filterResult.PreviouslyImported);
        }
    }
}
