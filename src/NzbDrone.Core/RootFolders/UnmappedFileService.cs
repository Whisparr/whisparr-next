using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Parser.RomanNumerals;

namespace NzbDrone.Core.RootFolders
{
    public class UnmappedFileSuggestion
    {
        public Movie Movie { get; set; }
        public bool HashMatch { get; set; }

        // The result's JAV code is the one read from the file name
        public bool CodeMatch { get; set; }

        // The cleaned title, or one of the alternative titles, equals the file's; with SameYear, the movie match
        public bool SameTitle { get; set; }
        public bool SameYear { get; set; }
        public bool SameDate { get; set; }
        public bool SameStudio { get; set; }
        public int TitleSimilarity { get; set; }
    }

    public class UnmappedFileSuggestions
    {
        public string Term { get; set; }
        public List<UnmappedFileSuggestion> Results { get; set; }
    }

    public interface IUnmappedFileService
    {
        PagingSpec<ImportFile> Paged(PagingSpec<ImportFile> pagingSpec);
        UnmappedFileSuggestions Suggest(int id, string term, ItemType itemType);
        Movie Link(int id, string foreignId, ItemType itemType, int qualityProfileId, bool monitored);
        void SetDismissed(List<int> ids, bool dismissed);
    }

    // Suggestions only rank what the search returns; a file is linked when the user picks one, never by itself.
    public class UnmappedFileService : IUnmappedFileService
    {
        private const int MaxHashLookups = 3;

        private static readonly Regex TrailingTagsRegex = new Regex(@"\s*(?:[\[\(]|\b(?:\d{3,4}p|4k|web[- ]?dl|web[- ]?rip|blu[- ]?ray|hdtv|xxx)\b).*$", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexDefaults.Timeout);

        private static readonly Dictionary<string, string> RomanNumerals = RomanNumeralParser.GetArabicRomanNumeralsMapping()
            .ToDictionary(n => n.ArabicNumeralAsString, n => n.RomanNumeralLowerCase);

        private static readonly Regex WholeNumberRegex = new Regex(@"\b\d+\b", RegexOptions.Compiled, RegexDefaults.Timeout);

        private static readonly Regex LeadingTagsRegex = new Regex(@"^(?:\s*(?:\[[^\]]*\]|\([^\)]*\)))+\s*", RegexOptions.Compiled, RegexDefaults.Timeout);
        private static readonly Regex SeparatorsRegex = new Regex(@"[._]+", RegexOptions.Compiled, RegexDefaults.Timeout);

        private readonly IImportFileRepository _importFileRepository;
        private readonly ISearchForNewMovie _searchProxy;
        private readonly IMovieService _movieService;
        private readonly IAddMovieService _addMovieService;
        private readonly IRefreshNewScenes _refreshNewScenes;
        private readonly IRootFolderService _rootFolderService;
        private readonly ILegacyFileLinker _fileLinker;
        private readonly IDiskProvider _diskProvider;
        private readonly IJavAvailability _javAvailability;
        private readonly Logger _logger;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor injection")]
        public UnmappedFileService(IImportFileRepository importFileRepository,
                                   ISearchForNewMovie searchProxy,
                                   IMovieService movieService,
                                   IAddMovieService addMovieService,
                                   IRefreshNewScenes refreshNewScenes,
                                   IRootFolderService rootFolderService,
                                   ILegacyFileLinker fileLinker,
                                   IDiskProvider diskProvider,
                                   IJavAvailability javAvailability,
                                   Logger logger)
        {
            _javAvailability = javAvailability;
            _logger = logger;
            _importFileRepository = importFileRepository;
            _searchProxy = searchProxy;
            _movieService = movieService;
            _addMovieService = addMovieService;
            _refreshNewScenes = refreshNewScenes;
            _rootFolderService = rootFolderService;
            _fileLinker = fileLinker;
            _diskProvider = diskProvider;
        }

        public PagingSpec<ImportFile> Paged(PagingSpec<ImportFile> pagingSpec)
        {
            return _importFileRepository.GetPaged(pagingSpec);
        }

        public UnmappedFileSuggestions Suggest(int id, string term, ItemType itemType)
        {
            var file = _importFileRepository.Get(id);
            var parsed = Parse(file);
            var fileTitle = GetSearchTerm(file, parsed);
            var hashIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var movies = new List<Movie>();
            var javAvailable = _javAvailability.IsAvailable();
            string javCode = null;
            var hasJavCode = javAvailable && TryGetJavCode(file, parsed, out javCode);

            // A JAV lookup by title rarely hits, so JAV mode searches the file's code unless something was typed
            var defaultTerm = itemType == ItemType.Jav && hasJavCode ? javCode : fileTitle;
            var searchTerm = term.IsNotNullOrWhiteSpace() ? term.Trim() : defaultTerm;

            if (itemType == ItemType.Jav && !javAvailable)
            {
                return new UnmappedFileSuggestions { Term = searchTerm, Results = new List<UnmappedFileSuggestion>() };
            }

            var javIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (itemType != ItemType.Jav && hasJavCode)
            {
                var javResults = SearchJavCode(javCode);

                movies.AddRange(javResults);
                javResults.ForEach(m => javIds.Add(m.ForeignId));
            }

            foreach (var candidate in (file.Candidates ?? []).Where(c => c.SceneId.IsNotNullOrWhiteSpace()).Take(MaxHashLookups))
            {
                hashIds.Add(candidate.SceneId);

                if (candidate.LookupTerm.IsNotNullOrWhiteSpace())
                {
                    movies.AddRange(Search(candidate.LookupTerm, ItemType.Scene));
                }
            }

            movies.AddRange(Search(searchTerm, itemType));

            var scored = movies
                .DistinctBy(m => m.ForeignId, StringComparer.OrdinalIgnoreCase)
                .Select(m => Score(m, parsed, fileTitle, hashIds, hasJavCode ? javCode : null))
                .ToList();

            ClearSharedCodeMatches(scored);

            return new UnmappedFileSuggestions
            {
                Term = searchTerm,
                Results = scored
                    .OrderByDescending(s => s.HashMatch)
                    .ThenByDescending(s => s.CodeMatch)
                    .ThenByDescending(s => javIds.Contains(s.Movie.ForeignId))
                    .ThenByDescending(s => s.SameDate)
                    .ThenByDescending(s => s.SameTitle && s.SameYear)
                    .ThenByDescending(s => s.TitleSimilarity)
                    .ThenByDescending(s => s.SameStudio)
                    .ToList()
            };
        }

        public Movie Link(int id, string foreignId, ItemType itemType, int qualityProfileId, bool monitored)
        {
            var file = _importFileRepository.Get(id);

            // Checked first, so a dead row doesn't leave a new scene behind
            if (!_diskProvider.FileExists(file.Path))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "{0} is no longer on disk", file.Path);
            }

            var movie = _movieService.FindByForeignId(foreignId);
            var isNew = movie == null;

            if (isNew)
            {
                var stub = new Movie
                {
                    ForeignId = foreignId,
                    Monitored = monitored,
                    QualityProfileId = qualityProfileId,
                    RootFolderPath = _rootFolderService.GetBestRootFolderPath(file.Path),
                    Path = Path.GetDirectoryName(file.Path),
                    AddOptions = new AddMovieOptions { SearchForMovie = false }
                };

                stub.MovieMetadata.Value.ItemType = itemType;

                // Inline, so no refresh queued by the add is still running with a stale movie when the file is linked
                movie = _addMovieService.AddMovies(new List<Movie> { stub }, false, refreshInline: true).FirstOrDefault()
                    ?? _movieService.FindByForeignId(foreignId)
                    ?? throw new NzbDroneClientException(HttpStatusCode.Conflict, "{0} could not be added", foreignId);
            }

            var outcome = _fileLinker.Link(movie, file.Path, f => Fill(f, file), MatchTier.Manual);

            // An inline add leaves the refresh to its caller, and only the refresh writes the credits
            if (isNew)
            {
                _refreshNewScenes.RefreshNewScenes(new List<Movie> { movie });
            }

            switch (outcome)
            {
                case LinkOutcome.Linked:
                case LinkOutcome.AlreadyLinked:
                    _importFileRepository.Delete(id);
                    return movie;
                case LinkOutcome.SceneHasOtherFile:
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "{0} already has a file", movie.Title);
                case LinkOutcome.LinkedElsewhere:
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "{0} is already linked to another item", file.Path);
                default:
                    throw new NzbDroneClientException(HttpStatusCode.Conflict, "{0} could not be linked: {1}", file.Path, outcome);
            }
        }

        public void SetDismissed(List<int> ids, bool dismissed)
        {
            var files = _importFileRepository.Get(ids).ToList();

            files.ForEach(f => f.Dismissed = dismissed);
            _importFileRepository.SaveDismissed(files);
        }

        // An extra lookup alongside the chosen type's search, so a failure leaves that search's results
        private List<Movie> SearchJavCode(string javCode)
        {
            try
            {
                return Search(javCode, ItemType.Jav);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "JAV search for {0} failed; suggesting scenes only", javCode);

                return new List<Movie>();
            }
        }

        private List<Movie> Search(string term, ItemType itemType)
        {
            return _searchProxy.SearchForNewEntity(term, itemType).OfType<Movie>().ToList();
        }

        private static UnmappedFileSuggestion Score(Movie movie, ParsedMovieInfo parsed, string fileTitle, HashSet<string> hashIds, string fileCode)
        {
            var releaseDate = movie.GetReleaseDate()?.ToString("yyyy-MM-dd");
            var studio = movie.MovieMetadata.Value.StudioTitle;

            return new UnmappedFileSuggestion
            {
                Movie = movie,
                HashMatch = hashIds.Contains(movie.ForeignId),
                CodeMatch = fileCode != null && JavCode.TryNormalize(movie.MovieMetadata.Value.Code, out var code) && code == fileCode,
                SameTitle = IsSameTitle(fileTitle, movie.MovieMetadata.Value),
                SameYear = parsed != null && parsed.Year > 0 && parsed.Year == movie.MovieMetadata.Value.Year,
                SameDate = parsed?.ReleaseDate != null && parsed.ReleaseDate == releaseDate,
                SameStudio = parsed?.StudioTitle != null && studio != null && parsed.StudioTitle.CleanMovieTitle() == studio.CleanMovieTitle(),
                TitleSimilarity = GetTitleSimilarity(fileTitle, movie.Title)
            };
        }

        // A code two titles share (a mis-tagged entry at the source) can't say which one the file is
        private static void ClearSharedCodeMatches(List<UnmappedFileSuggestion> suggestions)
        {
            if (suggestions.Count(s => s.CodeMatch) > 1)
            {
                suggestions.ForEach(s => s.CodeMatch = false);
            }
        }

        private static bool IsSameTitle(string fileTitle, MovieMetadata metadata)
        {
            var clean = CleanForTitleMatch(fileTitle);

            if (clean.IsNullOrWhiteSpace())
            {
                return false;
            }

            var titles = new[] { metadata.Title }
                .Concat((metadata.AlternativeTitles ?? new List<AlternativeTitle>()).Select(t => t.Title));

            return titles.Any(t => CleanForTitleMatch(t) == clean);
        }

        // "Pirates 2" and "Pirates II" are the same title. Only whole numbers are converted, before cleaning
        // strips the spaces, so "21" never reads as "2" + "1" ("iii", the same as "3")
        private static string CleanForTitleMatch(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var converted = WholeNumberRegex.Replace(title, m => RomanNumerals.TryGetValue(m.Value, out var roman) ? roman : m.Value);

            return converted.CleanMovieTitle();
        }

        internal static int GetTitleSimilarity(string a, string b)
        {
            if (a.IsNullOrWhiteSpace() || b.IsNullOrWhiteSpace())
            {
                return 0;
            }

            var longest = Math.Max(a.Length, b.Length);
            var distance = a.ToLowerInvariant().LevenshteinDistance(b.ToLowerInvariant());

            return Math.Max(0, 100 - (distance * 100 / longest));
        }

        internal static string GetSearchTerm(ImportFile file, ParsedMovieInfo parsed)
        {
            var term = CleanSearchTerm(parsed?.ReleaseTokens ?? parsed?.PrimaryMovieTitle);

            // A parse can leave only a tag as the title, e.g. "[Tag] (2024) ..." reads as a movie titled "[Tag]"
            return term.IsNotNullOrWhiteSpace() ? term : CleanSearchTerm(Path.GetFileNameWithoutExtension(file.Path));
        }

        private static string CleanSearchTerm(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            // Leading tags go first, or the trailing-tag cut starts at the first '[' and leaves nothing
            var untagged = LeadingTagsRegex.Replace(SeparatorsRegex.Replace(title, " "), string.Empty);

            return TrailingTagsRegex.Replace(untagged, string.Empty).Trim(' ', '-');
        }

        private static bool TryGetJavCode(ImportFile file, ParsedMovieInfo parsed, out string code)
        {
            return JavCode.TryNormalize(parsed?.Code, out code) || JavCode.TryExtract(Path.GetFileName(file.Path), out code);
        }

        private static ParsedMovieInfo Parse(ImportFile file)
        {
            return Parser.Parser.ParseMovieTitle(Path.GetFileName(file.Path));
        }

        private void Fill(MovieFile movieFile, ImportFile file)
        {
            movieFile.Size = _diskProvider.GetFileSize(file.Path);
            movieFile.Quality = QualityParser.ParseQuality(file.Name);
            movieFile.Languages = new List<Language> { Language.Unknown };
            movieFile.OriginalFilePath = file.Path;
            movieFile.DateAdded = DateTime.UtcNow;
        }
    }
}
