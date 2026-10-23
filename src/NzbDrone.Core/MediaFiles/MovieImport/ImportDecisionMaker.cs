using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MetadataSource.SkyHook.Fingerprints;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport
{
    public interface IMakeImportDecision
    {
        List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie);
        (List<Movie> MoviesToAdd, List<MovieFile> UnmappedFiles, List<ImportDecision> Decisions) GetSceneImportDecisions(List<string> videoFiles);
        List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, bool filterExistingFiles);
        List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, DownloadClientItem downloadClientItem, ParsedMovieInfo folderInfo, bool sceneSource);
        List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, DownloadClientItem downloadClientItem, ParsedMovieInfo folderInfo, bool sceneSource, bool filterExistingFiles);
        ImportDecision GetDecision(LocalMovie localMovie, DownloadClientItem downloadClientItem);
    }

    public class ImportDecisionMaker : IMakeImportDecision
    {
        private readonly IEnumerable<IImportDecisionEngineSpecification> _specifications;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMovieService _movieService;
        private readonly IAggregationService _aggregationService;
        private readonly ISceneIdentificationService _identificationService;
        private readonly IDiskProvider _diskProvider;
        private readonly IDetectSample _detectSample;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly ILocalMovieCustomFormatCalculationService _formatCalculator;
        private readonly IVideoFileInfoReader _videoFileInfoReader;
        private readonly IVideoHashService _videoHashService;
        private readonly IConfigService _configService;
        private readonly IFingerprintLookupClient _fingerprintLookupClient;
        private readonly IFileMatchEvaluator _fileMatchEvaluator;
        private readonly ILocalizationService _localizationService;
        private readonly Logger _logger;

        public ImportDecisionMaker(IEnumerable<IImportDecisionEngineSpecification> specifications,
                                   IMediaFileService mediaFileService,
                                   IMovieService movieService,
                                   IAggregationService aggregationService,
                                   ISceneIdentificationService identificationService,
                                   IDiskProvider diskProvider,
                                   IDetectSample detectSample,
                                   ITrackedDownloadService trackedDownloadService,
                                   ILocalMovieCustomFormatCalculationService formatCalculator,
                                   IVideoFileInfoReader videoFileInfoReader,
                                   IVideoHashService videoHashService,
                                   IConfigService configService,
                                   IFingerprintLookupClient fingerprintLookupClient,
                                   IFileMatchEvaluator fileMatchEvaluator,
                                   ILocalizationService localizationService,
                                   Logger logger)
        {
            _specifications = specifications;
            _mediaFileService = mediaFileService;
            _movieService = movieService;
            _aggregationService = aggregationService;
            _diskProvider = diskProvider;
            _detectSample = detectSample;
            _identificationService = identificationService;
            _trackedDownloadService = trackedDownloadService;
            _formatCalculator = formatCalculator;
            _videoFileInfoReader = videoFileInfoReader;
            _videoHashService = videoHashService;
            _configService = configService;
            _fingerprintLookupClient = fingerprintLookupClient;
            _fileMatchEvaluator = fileMatchEvaluator;
            _localizationService = localizationService;
            _logger = logger;
        }

        public List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie)
        {
            return GetImportDecisions(videoFiles, movie, null, null, false);
        }

        public (List<Movie> MoviesToAdd, List<MovieFile> UnmappedFiles, List<ImportDecision> Decisions) GetSceneImportDecisions(List<string> videoFiles)
        {
            return GetSceneImportDecisions(videoFiles, null, null, false, true);
        }

        public List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, bool filterExistingFiles)
        {
            return GetImportDecisions(videoFiles, movie, null, null, false, filterExistingFiles);
        }

        public List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, DownloadClientItem downloadClientItem, ParsedMovieInfo folderInfo, bool sceneSource)
        {
            return GetImportDecisions(videoFiles, movie, downloadClientItem, folderInfo, sceneSource, true);
        }

        public List<ImportDecision> GetImportDecisions(List<string> videoFiles, Movie movie, DownloadClientItem downloadClientItem, ParsedMovieInfo folderInfo, bool sceneSource, bool filterExistingFiles)
        {
            var newFiles = filterExistingFiles ? _mediaFileService.FilterExistingFiles(videoFiles.ToList(), movie) : videoFiles.ToList();

            _logger.Debug("Analyzing {0}/{1} files.", newFiles.Count, videoFiles.Count);

            ParsedMovieInfo downloadClientItemInfo = null;

            if (downloadClientItem != null)
            {
                downloadClientItemInfo = Parser.Parser.ParseMovieTitle(downloadClientItem.Title);
            }

            var nonSampleVideoFileCount = GetNonSampleVideoFileCount(newFiles, movie.MovieMetadata);

            var decisions = new List<ImportDecision>();

            var localMovies = newFiles.Select(file => new LocalMovie
            {
                Movie = movie,
                DownloadClientMovieInfo = downloadClientItemInfo,
                DownloadItem = downloadClientItem,
                FolderMovieInfo = folderInfo,
                Path = file,
                SceneSource = sceneSource,
                ExistingFile = movie.Path.IsParentPath(file),
                OtherVideoFiles = nonSampleVideoFileCount > 1
            }).ToList();

            // One local-only lookup for the whole batch, before the per-file decisions
            var lookups = LookupFingerprints(localMovies, movie);

            foreach (var localMovie in localMovies)
            {
                decisions.AddIfNotNull(GetDecision(localMovie, downloadClientItem, nonSampleVideoFileCount > 1, lookups));
            }

            return decisions;
        }

        // Null (not an empty dictionary) when fingerprinting doesn't apply, which is how movies skip it entirely.
        private Dictionary<string, FingerprintLookupResult> LookupFingerprints(List<LocalMovie> localMovies, Movie movie)
        {
            try
            {
                if (localMovies.Count == 0 || movie.MovieMetadata?.Value?.ItemType != ItemType.Scene)
                {
#pragma warning disable S1168
                    return null;
#pragma warning restore S1168
                }

                var files = new List<FileFingerprint>();
                var keys = new HashSet<string>();

                foreach (var localMovie in localMovies)
                {
                    var (osHash, hashedAt) = _videoHashService.ComputeOsHashStamp(_diskProvider, localMovie.Path, _logger);

                    localMovie.OsHash = osHash;
                    localMovie.HashedFileModifiedAt = hashedAt;

                    if (osHash.IsNotNullOrWhiteSpace() && keys.Add(localMovie.Path))
                    {
                        files.Add(new FileFingerprint { Key = localMovie.Path, OsHash = osHash });
                    }
                }

                if (files.Count == 0)
                {
                    return new Dictionary<string, FingerprintLookupResult>();
                }

                return _fingerprintLookupClient.Lookup(files, true) ?? new Dictionary<string, FingerprintLookupResult>();
            }
            catch (Exception ex)
            {
                // Nothing in the fingerprint phase fails or delays an import; no map means "no fingerprint data"
                _logger.Debug(ex, "Fingerprint lookup failed for {0} files", localMovies.Count);

#pragma warning disable S1168
                return null;
#pragma warning restore S1168
            }
        }

        // One upstream lookup for the files left unmapped; a Hash-tier outcome swaps the unmapped file for a normal decision on the in-library scene.
        private void LinkUnmappedByHash(List<(string Path, MovieFile File)> pending,
                                        List<MovieFile> unmappedFiles,
                                        List<ImportDecision> decisions,
                                        DownloadClientItem downloadClientItem,
                                        ParsedMovieInfo folderInfo,
                                        ParsedMovieInfo downloadClientItemInfo,
                                        bool sceneSource)
        {
            if (pending.Count == 0)
            {
                return;
            }

            var lookups = LookupUnmappedUpstream(pending);

            // Scenes the parser/identification batch already imports count too, so one scene never gets two files
            var linkedMovieIds = decisions.Where(d => d.Approved && d.LocalMovie.Movie != null)
                                          .Select(d => d.LocalMovie.Movie.Id)
                                          .ToHashSet();

            foreach (var (path, unmappedFile) in pending)
            {
                var target = FindHashTarget(path, unmappedFile, lookups);

                // A second file for an already-linked scene is a duplicate; leave it unmapped so the user sees it
                if (target == null || linkedMovieIds.Contains(target.Id))
                {
                    continue;
                }

                var localMovie = new LocalMovie
                {
                    Movie = target,
                    DownloadClientMovieInfo = downloadClientItemInfo,
                    DownloadItem = downloadClientItem,
                    FolderMovieInfo = folderInfo,
                    Path = path,
                    SceneSource = sceneSource,
                    MatchTier = MatchTier.Hash,
                    OsHash = unmappedFile.OsHash,
                    HashedFileModifiedAt = unmappedFile.HashedFileModifiedAt
                };

                var decision = GetDecision(localMovie, downloadClientItem, false, null);

                // A rejected link leaves the file unmapped rather than dropping it from the list
                if (decision is not { Approved: true })
                {
                    continue;
                }

                linkedMovieIds.Add(target.Id);
                unmappedFiles.Remove(unmappedFile);
                decisions.Add(decision);
                _logger.Info("Linked {0} to {1} by oshash", path, target);
            }
        }

        private Dictionary<string, FingerprintLookupResult> LookupUnmappedUpstream(List<(string Path, MovieFile File)> pending)
        {
            var empty = new Dictionary<string, FingerprintLookupResult>();

            try
            {
                var files = pending.Where(p => p.File.OsHash.IsNotNullOrWhiteSpace())
                                   .GroupBy(p => p.Path)
                                   .Select(g => new FileFingerprint { Key = g.Key, OsHash = g.First().File.OsHash })
                                   .ToList();

                if (files.Count == 0)
                {
                    return empty;
                }

                return _fingerprintLookupClient.Lookup(files, false) ?? empty;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Upstream fingerprint lookup failed for {0} unmapped files", pending.Count);

                return empty;
            }
        }

        private Movie FindHashTarget(string path, MovieFile unmappedFile, Dictionary<string, FingerprintLookupResult> lookups)
        {
            if (!lookups.TryGetValue(path, out var lookup))
            {
                return null;
            }

            var seconds = unmappedFile.MediaInfo?.RunTime.TotalSeconds;

            try
            {
                var outcome = _fileMatchEvaluator.Evaluate(null, lookup, seconds > 0 ? seconds : null, false);

                // Hits never add scenes: only a scene already in the library qualifies
                return outcome is { Tier: MatchTier.Hash, Target.Id: > 0 } ? outcome.Target : null;
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to verify fingerprint match for {0}", path);

                return null;
            }
        }

        private FileMatchOutcome EvaluateMatch(LocalMovie localMovie, FingerprintLookupResult lookup, double? duration)
        {
            try
            {
                return _fileMatchEvaluator.Evaluate(localMovie.Movie, lookup, duration);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to verify fingerprint match for {0}", localMovie.Path);

                return null;
            }
        }

        // Records the match tier on the file; returns a rejection when the hash points at a different scene.
        private ImportRejection VerifyFingerprint(LocalMovie localMovie, Dictionary<string, FingerprintLookupResult> lookups)
        {
            lookups.TryGetValue(localMovie.Path, out var lookup);

            var seconds = localMovie.MediaInfo?.RunTime.TotalSeconds;
            var duration = seconds > 0 ? seconds : null;

            var outcome = EvaluateMatch(localMovie, lookup, duration);

            if (outcome == null)
            {
                localMovie.MatchTier = MatchTier.Name;

                return null;
            }

            localMovie.MatchTier = outcome.Tier;
            localMovie.FingerprintCandidates = outcome.Candidates ?? new List<FingerprintCandidate>();

            if (!outcome.Conflict)
            {
                return null;
            }

            // A file already in the scene's folder can't be left orphaned by a rescan: keep it linked, flagged as Conflict
            if (localMovie.ExistingFile)
            {
                return null;
            }

            return FingerprintConflictRejection.Create(_localizationService, localMovie.Movie, localMovie.FingerprintCandidates);
        }

        public (List<Movie> MoviesToAdd, List<MovieFile> UnmappedFiles, List<ImportDecision> Decisions) GetSceneImportDecisions(List<string> videoFiles, DownloadClientItem downloadClientItem, ParsedMovieInfo folderInfo, bool sceneSource, bool filterExistingFiles)
        {
            var newFiles = filterExistingFiles ? _mediaFileService.FilterExistingFiles(videoFiles.ToList()) : videoFiles.ToList();

            _logger.Debug("Analyzing {0}/{1} files.", newFiles.Count, videoFiles.Count);

            ParsedMovieInfo downloadClientItemInfo = null;

            if (downloadClientItem != null)
            {
                downloadClientItemInfo = Parser.Parser.ParseMovieTitle(downloadClientItem.Title);
            }

            var moviesToAdd = new List<Movie>();
            var unmappedFiles = new List<MovieFile>();
            var decisions = new List<ImportDecision>();
            var pendingHashes = new List<(string Path, MovieFile File)>();

            var i = 0;
            foreach (var file in newFiles)
            {
                i++;
                _logger.ProgressInfo($"Identifying scene {i}/{newFiles.Count}");
                _logger.Debug($"Identifying scene - {file}");
                var movie = GetRemoteMovie(file.ToString());

                var fileMovieInfo = Parser.Parser.ParseMoviePath(file) ?? new ParsedMovieInfo();
                var localMovie = new LocalMovie
                {
                    Movie = movie,
                    DownloadClientMovieInfo = downloadClientItemInfo,
                    DownloadItem = downloadClientItem,
                    FolderMovieInfo = folderInfo,
                    FileMovieInfo = fileMovieInfo,
                    Path = movie.Path,
                    SceneSource = sceneSource,
                };

                if (string.IsNullOrWhiteSpace(movie.ForeignId))
                {
                    // Add the movieFile if no match to a movie
                    var unmappedFile = new MovieFile();

                    unmappedFile.DateAdded = DateTime.UtcNow;
                    unmappedFile.Languages = new List<Languages.Language>();
                    unmappedFile.MediaInfo = _videoFileInfoReader.GetMediaInfo(file.ToString());
                    unmappedFile.OriginalFilePath = file.ToString();
                    unmappedFile.Size = _diskProvider.GetFileSize(file.ToString());

                    var (osHash, hashedAt) = _videoHashService.ComputeOsHashStamp(_diskProvider, file.ToString(), _logger);
                    unmappedFile.OsHash = osHash;
                    unmappedFile.HashedFileModifiedAt = hashedAt;

                    localMovie.Movie.MovieFile = unmappedFile;
                    localMovie.Path = file.ToString();

                    // Augment movie file so imported files have all additional information an automatic import would
                    localMovie = _aggregationService.Augment(localMovie, null);
                    unmappedFile.Quality = localMovie.Quality;

                    unmappedFiles.Add(unmappedFile);
                    pendingHashes.Add((file.ToString(), unmappedFile));
                    _logger.Info("Added unmapped decision.  No matching movie found for file: {0}", file);
                    continue;
                }

                if (movie.Id == 0)
                {
                    moviesToAdd.Add(movie);
                    continue;
                }

                decisions.AddIfNotNull(GetDecision(localMovie, downloadClientItem, false, null));
            }

            LinkUnmappedByHash(pendingHashes, unmappedFiles, decisions, downloadClientItem, folderInfo, downloadClientItemInfo, sceneSource);

            return (moviesToAdd, unmappedFiles, decisions);
        }

        public ImportDecision GetDecision(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            var reasons = _specifications.Select(c => EvaluateSpec(c, localMovie, downloadClientItem))
                                         .Where(c => c != null);

            return new ImportDecision(localMovie, reasons.ToArray());
        }

        private Movie GetRemoteMovie(string file)
        {
            var scene = new Movie();
            var fileMovieInfo = Parser.Parser.ParseMoviePath(file);

            if (fileMovieInfo == null)
            {
                return scene;
            }

            // Try to get match by searching for the scene
            scene = _identificationService.Identify(file, fileMovieInfo);

            return scene;
        }

        private static ImportDecision WithFingerprintRejection(ImportDecision decision, ImportRejection fingerprintRejection)
        {
            if (fingerprintRejection == null || decision == null)
            {
                return decision;
            }

            return new ImportDecision(decision.LocalMovie, decision.Rejections.Append(fingerprintRejection).ToArray());
        }

        private ImportDecision GetDecision(LocalMovie localMovie, DownloadClientItem downloadClientItem, bool otherFiles, Dictionary<string, FingerprintLookupResult> lookups)
        {
            ImportDecision decision = null;
            ImportRejection fingerprintRejection = null;

            try
            {
                var fileMovieInfo = Parser.Parser.ParseMoviePath(localMovie.Path);

                localMovie.FileMovieInfo = fileMovieInfo;
                localMovie.Size = _diskProvider.GetFileSize(localMovie.Path);

                _aggregationService.Augment(localMovie, downloadClientItem);

                if (localMovie.Movie.MovieMetadata.Value.ItemType == ItemType.Movie)
                {
                    _formatCalculator.UpdateMovieCustomFormats(localMovie);

                    decision = GetDecision(localMovie, downloadClientItem);
                }
                else
                {
                    if (downloadClientItem?.DownloadId.IsNotNullOrWhiteSpace() == true)
                    {
                        var trackedDownload = _trackedDownloadService.Find(downloadClientItem.DownloadId);

                        if (trackedDownload?.RemoteMovie?.Release?.IndexerFlags != null)
                        {
                            localMovie.IndexerFlags = trackedDownload.RemoteMovie.Release.IndexerFlags;
                        }
                    }

                    // Switch the movie so that the QualityProfile is populated
                    localMovie.Movie = _movieService.GetMovie(localMovie.Movie.Id);

                    if (lookups != null)
                    {
                        fingerprintRejection = VerifyFingerprint(localMovie, lookups);
                    }
                    else if (localMovie.MatchTier == MatchTier.Unknown)
                    {
                        // Matched by the parser or identification, with no hash data to confirm it
                        localMovie.MatchTier = MatchTier.Name;
                    }

                    _formatCalculator.UpdateMovieCustomFormats(localMovie);

                    decision = GetDecision(localMovie, downloadClientItem);
                }

                if (localMovie.MediaInfo?.RunTime != null && localMovie.MediaInfo.RunTime.TotalMinutes != 0 && localMovie.Movie.MovieMetadata.Value.Runtime > 0)
                {
                    var runtime = localMovie.MediaInfo.RunTime.TotalMinutes;
                    var limit = _configService.WhisparrValidateRuntimeLimit;

                    if (runtime < localMovie.Movie.MovieMetadata.Value.Runtime - limit || runtime > localMovie.Movie.MovieMetadata.Value.Runtime + limit)
                    {
                        var rejection = $"Runtime of {localMovie.Movie.MovieMetadata.Value.Runtime} expected but {runtime} Found";
                        if (runtime == -1)
                        {
                            rejection = "Corrupt file detected as Media Information was not able to be extracted.";
                        }

                        _logger.Warn($"{rejection} for {localMovie.Movie.ToString()}");
                        if (_configService.WhisparrValidateRuntime)
                        {
                            decision = new ImportDecision(localMovie, new ImportRejection(ImportRejectionReason.Error, $"Runtime of {localMovie.Movie.MovieMetadata.Value.Runtime} expected but {runtime} Found"));
                        }
                    }
                }

                decision = WithFingerprintRejection(decision, fingerprintRejection);
            }
            catch (AugmentingFailedException)
            {
                decision = new ImportDecision(localMovie, new ImportRejection(ImportRejectionReason.UnableToParse, "Unable to parse file"));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Couldn't import file. {0}", localMovie.Path);

                decision = new ImportDecision(localMovie, new ImportRejection(ImportRejectionReason.Error, "Unexpected error processing file"));
            }

            if (decision == null)
            {
                _logger.Error("Unable to make a decision on {0}", localMovie.Path);
            }
            else if (decision.Rejections.Any())
            {
                _logger.Debug("File rejected for the following reasons: {0}", string.Join(", ", decision.Rejections));
            }
            else
            {
                _logger.Debug("File accepted");
            }

            return decision;
        }

        private ImportRejection EvaluateSpec(IImportDecisionEngineSpecification spec, LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            try
            {
                var result = spec.IsSatisfiedBy(localMovie, downloadClientItem);

                if (!result.Accepted)
                {
                    return new ImportRejection(result.Reason, result.Message);
                }
            }
            catch (NotImplementedException e)
            {
                _logger.Warn(e, "Spec " + spec.ToString() + " currently does not implement evaluation for movies.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Couldn't evaluate decision on {0}", localMovie.Path);
                return new ImportRejection(ImportRejectionReason.DecisionError, $"{spec.GetType().Name}: {ex.Message}");
            }

            return null;
        }

        private int GetNonSampleVideoFileCount(List<string> videoFiles, MovieMetadata movie)
        {
            return videoFiles.Count(file =>
            {
                var sample = _detectSample.IsSample(movie, file);

                if (sample == DetectSampleResult.Sample)
                {
                    return false;
                }

                return true;
            });
        }
    }
}
