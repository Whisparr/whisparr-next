using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Extras.Metadata
{
    public class ExistingMetadataImporter : ImportExistingExtraFilesBase<MetadataFile>
    {
        private readonly IExtraFileService<MetadataFile> _metadataFileService;
        private readonly IParsingService _parsingService;
        private readonly ISceneFolderLayout _sceneFolderLayout;
        private readonly ISidecarOwnership _sidecarOwnership;
        private readonly Logger _logger;
        private readonly List<IMetadata> _consumers;

        public ExistingMetadataImporter(IExtraFileService<MetadataFile> metadataFileService,
                                        IEnumerable<IMetadata> consumers,
                                        IParsingService parsingService,
                                        ISceneFolderLayout sceneFolderLayout,
                                        ISidecarOwnership sidecarOwnership,
                                        IMediaFileService mediaFileService,
                                        IDiskProvider diskProvider,
                                        Logger logger)
        : base(metadataFileService, mediaFileService, diskProvider)
        {
            _metadataFileService = metadataFileService;
            _parsingService = parsingService;
            _sceneFolderLayout = sceneFolderLayout;
            _sidecarOwnership = sidecarOwnership;
            _logger = logger;
            _consumers = consumers.ToList();
        }

        public override int Order => 0;

        public override IEnumerable<ExtraFile> ProcessFiles(Movie movie, List<string> filesOnDisk, List<string> importedFiles, string fileNameBeforeRename, IReadOnlyDictionary<string, int> resolvedOwners = null)
        {
            _logger.Debug("Looking for existing metadata in {0}", movie.Path);

            var metadataFiles = new List<MetadataFile>();
            var filterResult = FilterAndClean(movie, filesOnDisk, importedFiles, fileNameBeforeRename is not null);

            // Only look up the folder's owners when there is a file to assign; a studio folder can hold thousands of items
            var shared = resolvedOwners != null || (filterResult.FilesOnDisk.Count > 0 && _sceneFolderLayout.IsShared(movie));
            var folderFiles = shared && resolvedOwners == null ? _sidecarOwnership.GetFolderFiles(movie) : null;

            foreach (var possibleMetadataFile in filterResult.FilesOnDisk)
            {
                // Don't process files that have known Subtitle file extensions (saves a bit of unnecessary processing)

                if (SubtitleFileExtensions.Extensions.Contains(Path.GetExtension(possibleMetadataFile)))
                {
                    continue;
                }

                var movieFileId = shared ? 0 : GetCurrentMovieFileId(movie);

                if (shared)
                {
                    var ownerId = FindOwnerFileId(movie, possibleMetadataFile, resolvedOwners, _sidecarOwnership, folderFiles);

                    if (ownerId == null)
                    {
                        _logger.Debug("Metadata file: {0} is not owned by {1}", possibleMetadataFile, movie);
                        continue;
                    }

                    movieFileId = ownerId.Value;
                }

                foreach (var consumer in _consumers)
                {
                    var metadata = consumer.FindMetadataFile(movie, possibleMetadataFile);

                    if (metadata == null)
                    {
                        continue;
                    }

                    if (metadata.Type == MetadataType.MovieImage ||
                        metadata.Type == MetadataType.MovieMetadata)
                    {
                        var minimalInfo = _parsingService.ParseMinimalPathMovieInfo(possibleMetadataFile);

                        if (minimalInfo == null)
                        {
                            _logger.Debug("Unable to parse extra file: {0}", possibleMetadataFile);
                            continue;
                        }

                        metadata.MovieFileId = movieFileId;
                    }

                    metadata.Extension = Path.GetExtension(possibleMetadataFile);

                    metadataFiles.Add(metadata);
                }
            }

            _logger.Trace("Found {0} existing metadata files", metadataFiles.Count);
            _metadataFileService.Upsert(metadataFiles);

            // Return files that were just imported along with files that were
            // previously imported so previously imported files aren't imported twice
            return metadataFiles.Concat(filterResult.PreviouslyImported);
        }
    }
}
