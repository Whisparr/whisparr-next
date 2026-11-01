using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Movies.Studios
{
    public interface IAddStudioService
    {
        Studio AddStudio(Studio newStudio, bool ignoreErrors = false);
        List<Studio> AddStudios(List<Studio> newStudios, bool ignoreErrors = false);
    }

    public class AddStudioService : IAddStudioService
    {
        private readonly IStudioService _studioService;
        private readonly IProvideMovieInfo _studioInfo;
        private readonly IMetadataSourceService _metadataSourceService;
        private readonly Logger _logger;

        public AddStudioService(IStudioService studioService,
                                   IProvideMovieInfo studioInfo,
                                   IMetadataSourceService metadataSourceService,
                                   Logger logger)
        {
            _metadataSourceService = metadataSourceService;
            _studioService = studioService;
            _studioInfo = studioInfo;
            _logger = logger;
        }

        public Studio AddStudio(Studio newStudio, bool ignoreErrors = false)
        {
            Ensure.That(newStudio, () => newStudio).IsNotNull();

            _metadataSourceService.EnsureLocked();

            var existingStudio = _studioService.FindByForeignId(newStudio.ForeignId);

            if (existingStudio != null)
            {
                return existingStudio;
            }

            newStudio = AddSkyhookData(newStudio);
            newStudio = SetPropertiesAndValidate(newStudio);

            _logger.Info("Adding Studio {0}", newStudio.Title);

            try
            {
                _studioService.AddStudio(newStudio);
            }
            catch (Exception ex)
            {
                if (!ignoreErrors)
                {
                    throw;
                }

                _logger.Error("Error Adding Studio {0}", newStudio.Title, ex);
            }

            return newStudio;
        }

        public List<Studio> AddStudios(List<Studio> newStudios, bool ignoreErrors = false)
        {
            _metadataSourceService.EnsureLocked();

            var added = DateTime.UtcNow;
            var studiosToAdd = new List<Studio>();
            var existingStudioForeignIds = new HashSet<string>(_studioService.AllStudioForeignIds());

            foreach (var m in newStudios)
            {
                _logger.Info("Adding Studio {0}", m.Title);

                try
                {
                    if (existingStudioForeignIds.Contains(m.ForeignId))
                    {
                        _logger.Debug("Foreign ID {0} was not added due to validation failure: Studio already exists in database", m.ForeignId);
                        continue;
                    }

                    if (studiosToAdd.Any(f => f.ForeignId == m.ForeignId))
                    {
                        _logger.Debug("Foreign ID {0} was not added due to validation failure: Studio already exists on list", m.ForeignId);
                        continue;
                    }

                    var studio = AddSkyhookData(m);
                    studio = SetPropertiesAndValidate(studio);

                    studio.Added = added;

                    studiosToAdd.Add(studio);
                }
                catch (ValidationException ex)
                {
                    if (!ignoreErrors)
                    {
                        throw;
                    }

                    _logger.Error(ex, "Studio {0} was not added due to validation failures. {1}", m.ForeignId, ex.Message);
                }
                catch (Exception ex)
                {
                    if (!ignoreErrors)
                    {
                        throw;
                    }

                    _logger.Error(ex, "Studio {0} was not added due to an exception. {1}", m.ForeignId, ex.Message);
                }
            }

            return _studioService.AddStudios(studiosToAdd);
        }

        private Studio AddSkyhookData(Studio newStudio)
        {
            var studio = new Studio();

            try
            {
                studio = _studioInfo.GetStudioInfo(newStudio.ForeignId);
            }
            catch (MovieNotFoundException ex)
            {
                _logger.Error(ex, "Studio {0} was not found in the metadata source.", newStudio.ForeignId);

                throw new ValidationException(new List<ValidationFailure>
                                              {
                                                  new ValidationFailure("StashId", $"A studio with this ID was not found: {newStudio.ForeignId}", newStudio.ForeignId)
                                              });
            }

            studio.ApplyChanges(newStudio);

            return studio;
        }

        private Studio SetPropertiesAndValidate(Studio newStudio)
        {
            newStudio.CleanTitle = newStudio.Title.CleanStudioTitle();
            newStudio.SortTitle = MovieTitleNormalizer.Normalize(newStudio.Title, newStudio.ForeignId);
            newStudio.Added = DateTime.UtcNow;

            return newStudio;
        }
    }
}
