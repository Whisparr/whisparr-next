using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.MediaFiles.Fingerprints
{
    public interface IFileMatchEvaluator
    {
        FileMatchOutcome Evaluate(Movie parserMatch, FingerprintLookupResult lookup, double? fileDurationSeconds);

        // De-duplicated per scene, best first, enriched with display info.
        List<FingerprintCandidate> BuildCandidates(FingerprintLookupResult lookup);
    }

    public class FileMatchEvaluator : IFileMatchEvaluator
    {
        internal const int MaxPHashDistance = 8;
        internal const double MaxDurationDifferenceSeconds = 5;
        internal const int MaxRemoteLookups = 5;

        private const string OsHash = "OSHASH";

        private readonly IMovieService _movieService;
        private readonly IProvideMovieInfo _movieInfo;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public FileMatchEvaluator(IMovieService movieService, IProvideMovieInfo movieInfo, IConfigService configService, Logger logger)
        {
            _configService = configService;
            _movieService = movieService;
            _movieInfo = movieInfo;
            _logger = logger;
        }

        public FileMatchOutcome Evaluate(Movie parserMatch, FingerprintLookupResult lookup, double? fileDurationSeconds)
        {
            var usable = UsableCandidates(lookup);
            var library = new Dictionary<string, Movie>(StringComparer.OrdinalIgnoreCase);
            var candidates = Build(usable, library);

            FileMatchOutcome outcome;

            if (parserMatch != null)
            {
                outcome = EvaluateWithParser(parserMatch, usable, candidates);
            }
            else
            {
                outcome = EvaluateWithoutParser(usable, library, candidates, fileDurationSeconds);
            }

            // Display info from the remote source is only worth a call when someone has to review the outcome
            if (outcome.NeedsReview)
            {
                EnrichRemote(candidates);
            }

            return outcome;
        }

        private static FileMatchOutcome EvaluateWithoutParser(List<FingerprintCandidateResult> usable, Dictionary<string, Movie> library, List<FingerprintCandidate> candidates, double? fileDurationSeconds)
        {
            var outcome = new FileMatchOutcome { Tier = MatchTier.Unknown, NeedsReview = true, Candidates = candidates };

            var osHashScenes = usable
                .Where(c => IsOsHash(c))
                .Select(c => c.SceneId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (osHashScenes.Count != 1 || !fileDurationSeconds.HasValue)
            {
                return outcome;
            }

            // A perceptual match for a different scene contradicts the exact hash: leave it to a person
            if (usable.Any(c => !IsOsHash(c) && !string.Equals(c.SceneId, osHashScenes[0], StringComparison.OrdinalIgnoreCase)))
            {
                return outcome;
            }

            var candidate = usable
                .Where(c => IsOsHash(c) && string.Equals(c.SceneId, osHashScenes[0], StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Submissions)
                .First();

            if (!library.TryGetValue(candidate.SceneId, out var movie) ||
                !candidate.Duration.HasValue ||
                Math.Abs(candidate.Duration.Value - fileDurationSeconds.Value) > MaxDurationDifferenceSeconds)
            {
                return outcome;
            }

            outcome.Tier = MatchTier.Hash;
            outcome.Target = movie;
            outcome.NeedsReview = false;

            return outcome;
        }

        public List<FingerprintCandidate> BuildCandidates(FingerprintLookupResult lookup)
        {
            var candidates = Build(UsableCandidates(lookup), new Dictionary<string, Movie>(StringComparer.OrdinalIgnoreCase));

            EnrichRemote(candidates);

            return candidates;
        }

        private void EnrichRemote(List<FingerprintCandidate> candidates)
        {
            var remoteLookups = 0;

            foreach (var candidate in candidates)
            {
                if (candidate.MovieId.HasValue)
                {
                    continue;
                }

                if (remoteLookups >= MaxRemoteLookups)
                {
                    break;
                }

                remoteLookups++;
                Enrich(candidate);
            }
        }

        private static FileMatchOutcome EvaluateWithParser(Movie parserMatch, List<FingerprintCandidateResult> usable, List<FingerprintCandidate> candidates)
        {
            if (usable.Count == 0)
            {
                return new FileMatchOutcome { Tier = MatchTier.Name, Target = parserMatch, Candidates = candidates };
            }

            var parserId = parserMatch.ForeignId;

            if (usable.Any(c => string.Equals(c.SceneId, parserId, StringComparison.OrdinalIgnoreCase)))
            {
                return new FileMatchOutcome { Tier = MatchTier.Confirmed, Target = parserMatch, Candidates = candidates };
            }

            return new FileMatchOutcome
            {
                Tier = MatchTier.Conflict,
                NeedsReview = true,
                Conflict = true,
                Candidates = candidates
            };
        }

        private static List<FingerprintCandidateResult> UsableCandidates(FingerprintLookupResult lookup)
        {
            if (lookup == null || lookup.Status != FingerprintLookupStatus.Matched || lookup.Candidates == null)
            {
                return new List<FingerprintCandidateResult>();
            }

            return lookup.Candidates
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.SceneId))
                .Where(c => IsOsHash(c) || c.Distance <= MaxPHashDistance)
                .ToList();
        }

        private static bool IsOsHash(FingerprintCandidateResult candidate)
        {
            return string.Equals(candidate.Algorithm, OsHash, StringComparison.OrdinalIgnoreCase);
        }

        private List<FingerprintCandidate> Build(List<FingerprintCandidateResult> usable, Dictionary<string, Movie> library)
        {
            var ordered = usable
                .OrderBy(c => IsOsHash(c) ? 0 : 1)
                .ThenBy(c => c.Distance)
                .ThenByDescending(c => c.Submissions)
                .GroupBy(c => c.SceneId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            // SkyHookProxy.TryGetLookupId accepts stash: under StashDB and tpdb: under TPDB for a direct scene lookup
            var lookupPrefix = _configService.WhisparrSceneMetadataSource == SceneMetadataType.Tpdb ? "tpdb:" : "stash:";

            var result = new List<FingerprintCandidate>(ordered.Count);
            foreach (var source in ordered)
            {
                var candidate = new FingerprintCandidate
                {
                    SceneId = source.SceneId,
                    LookupTerm = lookupPrefix + source.SceneId,
                    Algorithm = source.Algorithm,
                    Distance = source.Distance,
                    Submissions = source.Submissions,
                    Duration = source.Duration
                };

                var movie = FindInLibrary(source.SceneId);

                if (movie != null)
                {
                    library[source.SceneId] = movie;
                    candidate.MovieId = movie.Id;

                    var metadata = movie.MovieMetadata?.Value;
                    candidate.Title = metadata?.Title;
                    candidate.StudioTitle = metadata?.StudioTitle;
                    candidate.ReleaseDate = metadata?.ReleaseDate;
                }

                result.Add(candidate);
            }

            return result;
        }

        private Movie FindInLibrary(string sceneId)
        {
            var movie = _movieService.FindByForeignId(sceneId);

            var lower = sceneId.ToLowerInvariant();

            if (movie == null && lower != sceneId)
            {
                movie = _movieService.FindByForeignId(lower);
            }

            return movie;
        }

        private void Enrich(FingerprintCandidate candidate)
        {
            try
            {
                var info = _movieInfo.GetSceneInfo(candidate.SceneId);
                var metadata = info?.Item1;

                if (metadata == null)
                {
                    return;
                }

                candidate.Title = metadata.Title;
                candidate.StudioTitle = !string.IsNullOrWhiteSpace(metadata.StudioTitle) ? metadata.StudioTitle : info.Item2?.Title;
                candidate.ReleaseDate = metadata.ReleaseDate;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not fetch scene info for fingerprint candidate {0}", candidate.SceneId);
            }
        }
    }
}
