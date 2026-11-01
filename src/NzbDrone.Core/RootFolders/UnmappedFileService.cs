using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
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
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.RootFolders
{
    public class UnmappedFileSuggestion
    {
        public Movie Movie { get; set; }
        public bool HashMatch { get; set; }
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
        private static readonly Regex SeparatorsRegex = new Regex(@"[._]+", RegexOptions.Compiled, RegexDefaults.Timeout);

        private readonly IImportFileRepository _importFileRepository;
        private readonly ISearchForNewMovie _searchProxy;
        private readonly IMovieService _movieService;
        private readonly IAddMovieService _addMovieService;
        private readonly IRefreshNewScenes _refreshNewScenes;
        private readonly IRootFolderService _rootFolderService;
        private readonly ILegacyFileLinker _fileLinker;
        private readonly IDiskProvider _diskProvider;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Constructor injection")]
        public UnmappedFileService(IImportFileRepository importFileRepository,
                                   ISearchForNewMovie searchProxy,
                                   IMovieService movieService,
                                   IAddMovieService addMovieService,
                                   IRefreshNewScenes refreshNewScenes,
                                   IRootFolderService rootFolderService,
                                   ILegacyFileLinker fileLinker,
                                   IDiskProvider diskProvider)
        {
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
            var searchTerm = term.IsNotNullOrWhiteSpace() ? term.Trim() : GetSearchTerm(file, parsed);
            var fileTitle = GetSearchTerm(file, parsed);
            var hashIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var movies = new List<Movie>();

            foreach (var candidate in (file.Candidates ?? []).Where(c => c.SceneId.IsNotNullOrWhiteSpace()).Take(MaxHashLookups))
            {
                hashIds.Add(candidate.SceneId);

                if (candidate.LookupTerm.IsNotNullOrWhiteSpace())
                {
                    movies.AddRange(Search(candidate.LookupTerm, ItemType.Scene));
                }
            }

            movies.AddRange(Search(searchTerm, itemType));

            return new UnmappedFileSuggestions
            {
                Term = searchTerm,
                Results = movies
                    .DistinctBy(m => m.ForeignId, StringComparer.OrdinalIgnoreCase)
                    .Select(m => Score(m, parsed, fileTitle, hashIds))
                    .OrderByDescending(s => s.HashMatch)
                    .ThenByDescending(s => s.SameDate)
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

        private List<Movie> Search(string term, ItemType itemType)
        {
            return _searchProxy.SearchForNewEntity(term, itemType).OfType<Movie>().ToList();
        }

        private static UnmappedFileSuggestion Score(Movie movie, ParsedMovieInfo parsed, string fileTitle, HashSet<string> hashIds)
        {
            var releaseDate = movie.GetReleaseDate()?.ToString("yyyy-MM-dd");
            var studio = movie.MovieMetadata.Value.StudioTitle;

            return new UnmappedFileSuggestion
            {
                Movie = movie,
                HashMatch = hashIds.Contains(movie.ForeignId),
                SameDate = parsed?.ReleaseDate != null && parsed.ReleaseDate == releaseDate,
                SameStudio = parsed?.StudioTitle != null && studio != null && parsed.StudioTitle.CleanMovieTitle() == studio.CleanMovieTitle(),
                TitleSimilarity = GetTitleSimilarity(fileTitle, movie.Title)
            };
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
            var title = parsed?.ReleaseTokens ?? parsed?.PrimaryMovieTitle;

            if (title.IsNullOrWhiteSpace())
            {
                title = Path.GetFileNameWithoutExtension(file.Path);
            }

            return TrailingTagsRegex.Replace(SeparatorsRegex.Replace(title, " "), string.Empty).Trim(' ', '-');
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
