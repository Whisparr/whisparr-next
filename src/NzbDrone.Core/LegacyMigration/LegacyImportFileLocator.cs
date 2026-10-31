using System;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.LegacyMigration
{
    public interface ILegacyImportFileLocator
    {
        // The parked file, pointed at where it really is when the legacy path has gone out of date
        ImportFile Locate(ImportFile file);
    }

    // Eros only rescans a scene when it refreshes its metadata, so a file renamed outside the app keeps its old name in
    // the legacy database. The path is corrected only when the scene's folder leaves no doubt: exactly one video, and
    // nothing else claims it. Shared folders hold several, so they are left for the report.
    public class LegacyImportFileLocator : ILegacyImportFileLocator
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskScanService _diskScanService;
        private readonly IImportFileRepository _importFileRepository;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public LegacyImportFileLocator(IDiskProvider diskProvider,
                                       IDiskScanService diskScanService,
                                       IImportFileRepository importFileRepository,
                                       IMediaFileService mediaFileService,
                                       Logger logger)
        {
            _diskProvider = diskProvider;
            _diskScanService = diskScanService;
            _importFileRepository = importFileRepository;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public ImportFile Locate(ImportFile file)
        {
            if (file == null)
            {
                return null;
            }

            try
            {
                var actualPath = FindActualPath(file.Path);

                if (actualPath != null)
                {
                    Repoint(file, actualPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Same as a file that can't be found: hashing and linking report it
                _logger.Warn(ex, "Could not look for the file behind {0}", file.Path);
            }

            return file;
        }

        private string FindActualPath(string path)
        {
            if (_diskProvider.FileExists(path))
            {
                return null;
            }

            var folder = Path.GetDirectoryName(path);

            if (!_diskProvider.FolderExists(folder))
            {
                return null;
            }

            var videos = _diskScanService.GetVideoFiles(folder, false);

            if (videos.Length != 1)
            {
                return null;
            }

            var actualPath = videos[0];

            return _importFileRepository.FindByPath(actualPath) == null && _mediaFileService.FindByFullPath(actualPath) == null
                ? actualPath
                : null;
        }

        private void Repoint(ImportFile file, string actualPath)
        {
            _logger.Info("{0} is not on disk, using {1} from the same folder", file.Path, actualPath);

            var fileName = Path.GetFileName(actualPath);
            var relativeFolder = Path.GetDirectoryName(file.RelativePath ?? string.Empty);

            file.Path = actualPath;
            file.RelativePath = string.IsNullOrEmpty(relativeFolder) ? fileName : Path.Combine(relativeFolder, fileName);
            file.Name = Path.GetFileNameWithoutExtension(fileName);

            // Whatever the legacy row knew about was a different name; hash what's really there
            file.OsHash = null;
            file.HashedFileModifiedAt = null;
            file.PHash = null;
            file.PHashAttemptedAt = null;
            file.Candidates = null;
            file.CandidatesCheckedAt = null;

            _importFileRepository.Update(file);
        }
    }
}
