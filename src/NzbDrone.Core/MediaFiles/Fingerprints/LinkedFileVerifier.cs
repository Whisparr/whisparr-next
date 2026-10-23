using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.MediaFiles.Fingerprints
{
    public interface ILinkedFileVerifier
    {
        // Checks linked files that just got a phash against the local fingerprint index.
        // A candidate for the file's own scene confirms it, one for another scene flags a conflict.
        void Verify(IReadOnlyCollection<MovieFile> movieFiles);
    }

    public class LinkedFileVerifier : ILinkedFileVerifier
    {
        internal const int BatchSize = 100;

        // Skyhook already drops phash candidates further away than this
        internal const int MaxPHashDistance = 8;

        private readonly IFingerprintLookupClient _lookupClient;
        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public LinkedFileVerifier(IFingerprintLookupClient lookupClient,
                                  IMovieService movieService,
                                  IMediaFileService mediaFileService,
                                  Logger logger)
        {
            _lookupClient = lookupClient;
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public void Verify(IReadOnlyCollection<MovieFile> movieFiles)
        {
            // Manual is the user's decision and Confirmed already has name and oshash agreeing; Conflict is already flagged
            var files = movieFiles
                .Where(f => f.MovieId > 0 && f.PHash != null && f.MatchTier is MatchTier.Unknown or MatchTier.Name or MatchTier.Hash)
                .ToList();

            if (files.Count == 0)
            {
                return;
            }

            var movieIds = files.Select(f => f.MovieId).Distinct().ToList();
            var foreignIds = _movieService.GetMovies(movieIds).ToDictionary(m => m.Id, m => m.ForeignId);
            var changed = new List<MovieFile>();

            foreach (var batch in files.Chunk(BatchSize))
            {
                changed.AddRange(VerifyBatch(batch, foreignIds));
            }

            if (changed.Count > 0)
            {
                _mediaFileService.SaveMatchTiers(changed);
            }

            _logger.Debug("Perceptual hash verified {0} linked files, {1} changed", files.Count, changed.Count);
        }

        // Returns the files whose tier changed
        private List<MovieFile> VerifyBatch(MovieFile[] batch, Dictionary<int, string> foreignIds)
        {
            var fingerprints = batch.Select(f => new FileFingerprint { Key = f.Id.ToString(), PHash = f.PHash }).ToList();
            var results = _lookupClient.Lookup(fingerprints, true);
            var changed = new List<MovieFile>();

            foreach (var file in batch)
            {
                if (!results.TryGetValue(file.Id.ToString(), out var result) ||
                    !foreignIds.TryGetValue(file.MovieId, out var foreignId))
                {
                    continue;
                }

                var tier = Classify(result, foreignId);

                // A Hash link is flagged but never promoted: it stays Hash when the phash agrees
                if (tier == null || (tier == MatchTier.Confirmed && file.MatchTier == MatchTier.Hash))
                {
                    continue;
                }

                file.MatchTier = tier.Value;
                changed.Add(file);

                if (tier == MatchTier.Conflict)
                {
                    _logger.Info("Perceptual hash of movie file {0} matches another scene than its linked movie {1}", file.Id, file.MovieId);
                }
            }

            return changed;
        }

        // null means no usable candidate, so the file stays as it is
        private static MatchTier? Classify(FingerprintLookupResult result, string foreignId)
        {
            if (result.Status != FingerprintLookupStatus.Matched)
            {
                return null;
            }

            var candidates = result.Candidates
                .Where(c => !string.IsNullOrEmpty(c.SceneId) && c.Distance <= MaxPHashDistance)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates.Any(c => string.Equals(c.SceneId, foreignId, StringComparison.OrdinalIgnoreCase))
                ? MatchTier.Confirmed
                : MatchTier.Conflict;
        }
    }
}
