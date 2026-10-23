using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.MediaFiles.MovieImport
{
    // The rejection for a file whose hash points at a different scene than the one it was matched to.
    public static class FingerprintConflictRejection
    {
        public static ImportRejection Create(ILocalizationService localizationService, Movie matched, List<FingerprintCandidate> candidates)
        {
            var matchedId = matched?.ForeignId;
            var other = candidates.FirstOrDefault(c => !string.Equals(c.SceneId, matchedId, StringComparison.OrdinalIgnoreCase))
                        ?? candidates.FirstOrDefault();
            var title = other?.Title.IsNotNullOrWhiteSpace() == true ? other.Title : other?.SceneId;

            var message = localizationService.GetLocalizedString("FingerprintConflictRejection", new Dictionary<string, object> { { "title", title } });

            return new ImportRejection(ImportRejectionReason.FingerprintConflict, message);
        }
    }
}
