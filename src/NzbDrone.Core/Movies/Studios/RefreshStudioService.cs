using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.ImportLists.ImportExclusions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies.Jav;
using NzbDrone.Core.Movies.Studios.Commands;

namespace NzbDrone.Core.Movies.Studios
{
    public class RefreshStudioService : IExecute<RefreshStudiosCommand>
    {
        private readonly IProvideMovieInfo _movieInfo;
        private readonly IStudioService _studioService;
        private readonly IMovieService _movieService;
        private readonly IAddMovieService _addMovieService;
        private readonly ISceneCatalogImporter _sceneCatalogImporter;
        private readonly IConfigService _configService;
        private readonly IJavAvailability _javAvailability;
        private readonly IDiskScanService _diskScanService;
        private readonly IRescanAfterRefreshFilter _rescanAfterRefreshFilter;
        private readonly IImportListExclusionService _importListExclusionService;

        private readonly Logger _logger;

        public RefreshStudioService(IProvideMovieInfo movieInfo,
                                        IStudioService studioService,
                                        IMovieService movieService,
                                        IAddMovieService addMovieService,
                                        ISceneCatalogImporter sceneCatalogImporter,
                                        IConfigService configService,
                                        IJavAvailability javAvailability,
                                        IDiskScanService diskScanService,
                                        IRescanAfterRefreshFilter rescanAfterRefreshFilter,
                                        IImportListExclusionService importListExclusionService,
                                        Logger logger)
        {
            _movieInfo = movieInfo;
            _studioService = studioService;
            _movieService = movieService;
            _addMovieService = addMovieService;
            _sceneCatalogImporter = sceneCatalogImporter;
            _configService = configService;
            _javAvailability = javAvailability;
            _diskScanService = diskScanService;
            _rescanAfterRefreshFilter = rescanAfterRefreshFilter;
            _importListExclusionService = importListExclusionService;
            _logger = logger;
        }

        private Studio RefreshStudioInfo(int studioId)
        {
            // Get the movie before updating, that way any changes made to the movie after the refresh started,
            // but before this movie was refreshed won't be lost.
            var studio = _studioService.GetById(studioId);

            _logger.ProgressInfo("Updating info for {0}", studio.Title);

            Studio studioInfo;

            try
            {
                studioInfo = _movieInfo.GetStudioInfo(studio.ForeignId);
            }
            catch (MovieNotFoundException)
            {
                throw;
            }

            if (studio.ForeignId != studioInfo.ForeignId)
            {
                _logger.Warn("Studio '{0}' (StashDb: {1}) was replaced with '{2}' (StashDb: {3}), because the original was a duplicate.", studio.Title, studio.ForeignId, studio.Title, studio.ForeignId);
                studio.ForeignId = studioInfo.ForeignId;
            }

            studio.TmdbId = studioInfo.TmdbId;

            studio.TpdbId = studioInfo.TpdbId;
            studio.Title = studioInfo.Title;
            studio.Network = studioInfo.Network;
            studio.Website = studioInfo.Website;
            studio.Images = studioInfo.Images;
            studio.LastInfoSync = DateTime.UtcNow;
            studio.CleanTitle = studioInfo.CleanTitle;
            studio.SortTitle = studioInfo.SortTitle;
            studio.Aliases = studioInfo.Aliases;
            studio.Status = studioInfo.Status;

            // A null answer keeps the stored value; the next refresh tries again.
            var hasJav = studioInfo.HasJavReported ?? (_javAvailability.IsAvailable() ? _movieInfo.GetStudioHasJav(studio.ForeignId) : null);

            if (hasJav.HasValue)
            {
                studio.HasJav = hasJav.Value;
            }

            _studioService.Update(studio);

            // Aliases

            _logger.Debug("Finished studio metadata refresh for {0}", studio.Title);

            return studio;
        }

        private void SyncStudioItems(Studio studio, bool syncMovies)
        {
            if (!studio.Monitored && !studio.MoviesMonitored && !studio.JavMonitored)
            {
                return;
            }

            // Chunk the into smaller lists
            var chunkSize = 10;

            // The global master switch overrides the studio's own setting, so
            // turning it off leaves discovery intact but adds works unmonitored
            // and without a search on add.
            var monitorNewItems = _configService.WhisparrMonitorNewItems && studio.WhisparrMonitorNewItems;

            Movie BuildStub(string stashId) => new()
            {
                ForeignId = stashId,
                QualityProfileId = studio.QualityProfileId,
                RootFolderPath = studio.RootFolderPath,
                AddOptions = new AddMovieOptions
                {
                    SearchForMovie = studio.SearchOnAdd && monitorNewItems,
                    AddMethod = AddMovieMethod.Studio
                },
                Monitored = monitorNewItems,
                Tags = studio.Tags
            };

            if (studio.Monitored)
            {
                _sceneCatalogImporter.ImportScenes(
                    studio.Title,
                    cursor => _movieInfo.GetStudioScenePage(studio.ForeignId, cursor),
                    BuildStub,
                    () => _studioService.FindByForeignId(studio.ForeignId) is { Monitored: true });
            }

            if (studio.JavMonitored && studio.HasJav && _javAvailability.IsAvailable())
            {
                _sceneCatalogImporter.ImportScenes(
                    studio.Title,
                    cursor => _movieInfo.GetStudioJavPage(studio.ForeignId, cursor),
                    stashId =>
                    {
                        var stub = BuildStub(stashId);
                        stub.MovieMetadata.Value.ItemType = ItemType.Jav;

                        return stub;
                    },
                    () => _studioService.FindByForeignId(studio.ForeignId) is { JavMonitored: true });
            }

            if (studio.MoviesMonitored && syncMovies)
            {
                (List<string> StashdbIds, List<string> TpdbIds, List<int> TmdbIds) studioWork;

                try
                {
                    studioWork = _movieInfo.GetStudioWorks(studio.ForeignId);
                }
                catch (WebException ex)
                {
                    _logger.Warn(
                        ex,
                        "Unable to refresh works for studio '{0}' ({1}). Skipping studio.",
                        studio.Title,
                        studio.ForeignId);

                    return;
                }

                if (_configService.WhisparrMovieMetadataSource == MovieMetadataType.TMDB)
                {
                    var moviesAdded = 0;
                    var tmbdId = 0;
                    var existingMovies = _movieService.AllMovieTmdbIds();
                    var excludedMovies = _importListExclusionService.GetAllExclusions()
                        .Select(e => int.TryParse(e.ForeignId, out tmbdId))
                        .Select(e => tmbdId).Where(e => e != 0)
                        .ToList();

                    var moviesToAdd = studioWork.TmdbIds
                        .Where(m => !existingMovies.Contains(m)).Where(m => !excludedMovies.Contains(m));

                    if (moviesToAdd.Any())
                    {
                        var movieLists = moviesToAdd.Select(m => new Movie
                        {
                            ForeignId = m.ToString(),
                            TmdbId = m,
                            QualityProfileId = studio.QualityProfileId,
                            RootFolderPath = studio.RootFolderPath,
                            AddOptions = new AddMovieOptions
                            {
                                SearchForMovie = studio.SearchOnAdd && monitorNewItems,
                                AddMethod = AddMovieMethod.Studio
                            },
                            Monitored = monitorNewItems,
                            Tags = studio.Tags
                        }).Chunk(chunkSize);

                        foreach (var movieList in movieLists)
                        {
                            moviesAdded += _addMovieService.AddMovies(movieList.ToList(), true).Count;
                        }
                    }

                    _logger.Info("Synced studio {0} has {1} movies adding {2} and added {3}",
                        studio.Title,
                        studioWork.TmdbIds.Count,
                        moviesToAdd.Count(),
                        moviesAdded);
                }

                if (_configService.WhisparrMovieMetadataSource == MovieMetadataType.TPDB)
                {
                    var moviesAdded = 0;
                    var existingMovies = _movieService.AllMovieTpdbIds();
                    var excludedMovies = _importListExclusionService.GetAllExclusions().Where(e => e.Type == ImportExclusionType.Movie).Select(e => e.ForeignId).ToList();
                    var moviesToAdd = studioWork.TpdbIds.Where(m => !existingMovies.Contains(m)).Where(m => !excludedMovies.Contains(m));

                    if (moviesToAdd.Any())
                    {
                        var movieLists = moviesToAdd.Select(m => new Movie
                        {
                            ForeignId = $"tpdbid:{m}",
                            TpdbId = m,
                            QualityProfileId = studio.QualityProfileId,
                            RootFolderPath = studio.RootFolderPath,
                            AddOptions = new AddMovieOptions
                            {
                                SearchForMovie = studio.SearchOnAdd && monitorNewItems,
                                AddMethod = AddMovieMethod.Studio
                            },
                            Monitored = monitorNewItems,
                            Tags = studio.Tags
                        }).Chunk(chunkSize);

                        foreach (var movieList in movieLists)
                        {
                            moviesAdded += _addMovieService.AddMovies(movieList.ToList(), true).Count;
                        }
                    }

                    _logger.Info("Synced studio {0} has {1} movies adding {2} and added {3}",
                        studio.Title,
                        studioWork.TpdbIds.Count,
                        moviesToAdd.Count(),
                        moviesAdded);
                }
            }
        }

        public void Execute(RefreshStudiosCommand message)
        {
            // Decided once per refresh, so the reason is logged once rather than for every studio.
            var syncMovies = !TpdbMovieSync.IsBlocked(_configService);

            if (!syncMovies)
            {
                _logger.Info(TpdbMovieSync.NeedsTpdbMovieSourceMessage);
            }

            if (message.StudioIds.Any())
            {
                foreach (var studioId in message.StudioIds)
                {
                    var studio = _studioService.GetById(studioId);
                    var items = _movieService.GetByStudioForeignId(studio.ForeignId);
                    var trigger = message.Trigger;
                    var isNew = false;

                    try
                    {
                        studio = RefreshStudioInfo(studioId);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Couldn't refresh info for {0}", studio.Title);
                    }

                    // Rescan before sync since syncing will add a new movie and scan automatically
                    var scanIds = items
                        .Where(movieItem => _rescanAfterRefreshFilter.ShouldRescan(movieItem, isNew, trigger))
                        .Select(movieItem => movieItem.Id)
                        .ToList();

                    if (scanIds.Any())
                    {
                        _logger.ProgressInfo("Rescanning {0} items for {1}", scanIds.Count, studio.Title);

                        try
                        {
                            _diskScanService.Scan(scanIds, false);
                        }
                        catch (Exception e)
                        {
                            _logger.Error(e, "Couldn't rescan movies for {0}", studio.Title);
                        }
                    }

                    // The scene import and GetStudioWorks both let HTTP errors propagate
                    // (HttpException, or MovieNotFoundException on a 404). Without this,
                    // either one drops every studio the user selected after the one that failed.
                    try
                    {
                        // Replaces the rescan message, which would otherwise stay up through the sync
                        _logger.ProgressDebug("Syncing items for {0}", studio.Title);
                        SyncStudioItems(studio, syncMovies);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, "Couldn't sync items for {0}", studio.Title);
                    }
                }
            }
            else
            {
                var studioIdChunks = _studioService.AllStudioIdsByLastInfoSync().Chunk(1000);

                var updatedStudios = new HashSet<string>();

                if (message.LastStartTime.HasValue && message.LastStartTime.Value.AddDays(14) > DateTime.UtcNow)
                {
                    updatedStudios = _movieInfo.GetChangedStudios(message.LastStartTime.Value);
                }

                foreach (var chunk in studioIdChunks)
                {
                    foreach (var studio in _studioService.GetStudios(chunk))
                    {
                        try
                        {
                            if ((updatedStudios.Count == 0 && studio.LastInfoSync < DateTime.UtcNow.AddDays(-14)) ||
                                updatedStudios.Contains(studio.ForeignId) ||
                                message.Trigger == CommandTrigger.Manual)
                            {
                                RefreshStudioInfo(studio.Id);
                            }

                            SyncStudioItems(studio, syncMovies);
                        }
                        catch (MovieNotFoundException)
                        {
                            _logger.Error("Studio '{0}' (StashDb {1}) was not found, it may have been removed from The Movie Database.", studio.Title, studio.ForeignId);
                        }
                        catch (Exception e)
                        {
                            _logger.Error(e, "Couldn't refresh info for {0}", studio.Title);
                        }
                    }
                }
            }
        }
    }
}
