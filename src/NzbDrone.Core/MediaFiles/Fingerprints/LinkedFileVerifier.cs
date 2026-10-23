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
            var files = movieFiles.Where(IsVerifiable).ToList();

            if (files.Count == 0)
            {
                return;
            }

            // Only scenes have fingerprints to check against
            var movieIds = files.Select(f => f.MovieId).Distinct().ToList();
            var foreignIds = _movieService.GetMovies(movieIds)
                                          .Where(m => m.MovieMetadata?.Value?.ItemType == ItemType.Scene)
                                          .ToDictionary(m => m.Id, m => m.ForeignId);

            files = files.Where(f => foreignIds.ContainsKey(f.MovieId)).ToList();

            if (files.Count == 0)
            {
                return;
            }

            var results = new Dictionary<string, FingerprintLookupResult>();

            foreach (var batch in files.Chunk(BatchSize))
            {
                var fingerprints = batch.Select(f => new FileFingerprint { Key = f.Id.ToString(), PHash = f.PHash }).ToList();

                foreach (var (key, result) in _lookupClient.Lookup(fingerprints, true))
                {
                    results[key] = result;
                }
            }

            // The caller's rows were read before the (slow) phash run; classify the current ones so a tier or
            // movie change made meanwhile isn't overwritten
            var current = _mediaFileService.GetMovies(files.Select(f => f.Id).ToList()).ToDictionary(f => f.Id);
            var changed = new List<MovieFile>();

            foreach (var file in files)
            {
                if (current.TryGetValue(file.Id, out var fresh) && fresh.MovieId == file.MovieId && IsVerifiable(fresh))
                {
                    changed.AddRange(Verify(fresh, results, foreignIds));
                }
            }

            if (changed.Count > 0)
            {
                _mediaFileService.SaveMatchTiers(changed);
            }

            _logger.Debug("Perceptual hash verified {0} linked files, {1} changed", files.Count, changed.Count);
        }

        // Manual is the user's decision and Confirmed already has name and oshash agreeing; Conflict is already flagged
        private static bool IsVerifiable(MovieFile file)
        {
            return file.MovieId > 0 && file.PHash != null && file.MatchTier is MatchTier.Unknown or MatchTier.Name or MatchTier.Hash;
        }

        // Returns the file when its tier changed, nothing otherwise
        private IEnumerable<MovieFile> Verify(MovieFile file, Dictionary<string, FingerprintLookupResult> results, Dictionary<int, string> foreignIds)
        {
            if (!results.TryGetValue(file.Id.ToString(), out var result) ||
                !foreignIds.TryGetValue(file.MovieId, out var foreignId))
            {
                return Enumerable.Empty<MovieFile>();
            }

            var tier = Classify(result, foreignId);

            // A Hash link is flagged but never promoted: it stays Hash when the phash agrees
            if (tier == null || (tier == MatchTier.Confirmed && file.MatchTier == MatchTier.Hash))
            {
                return Enumerable.Empty<MovieFile>();
            }

            file.MatchTier = tier.Value;

            if (tier == MatchTier.Conflict)
            {
                _logger.Info("Perceptual hash of movie file {0} matches another scene than its linked movie {1}", file.Id, file.MovieId);
            }

            return new[] { file };
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
