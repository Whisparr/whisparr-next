using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.MovieImport.Manual
{
    public class ManualImportItem
    {
        public string Path { get; set; }
        public string RelativePath { get; set; }
        public string FolderName { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public Movie Movie { get; set; }
        public int? MovieFileId { get; set; }
        public QualityModel Quality { get; set; } = new();
        public List<Language> Languages { get; set; } = new();
        public string ReleaseGroup { get; set; }
        public string DownloadId { get; set; }
        public List<CustomFormat> CustomFormats { get; set; } = new();
        public int CustomFormatScore { get; set; }
        public int IndexerFlags { get; set; }
        public IEnumerable<ImportRejection> Rejections { get; set; } = new List<ImportRejection>();
        public List<FingerprintCandidate> FingerprintCandidates { get; set; } = new();

        // The movie the file's hash contradicts, so a reprocess can tell whether the user has since picked another one.
        public int? FingerprintConflictMovieId =>
            Movie != null && Rejections.Any(r => r.Reason == ImportRejectionReason.FingerprintConflict) ? Movie.Id : null;
    }
}
