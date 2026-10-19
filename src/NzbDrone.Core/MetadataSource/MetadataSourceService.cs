using System;
using NzbDrone.Common.Exceptions;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource
{
    public class MetadataSources
    {
        public SceneMetadataType Scene { get; set; }
        public MovieMetadataType Movie { get; set; }
        public bool Locked { get; set; }
    }

    public class MetadataSourcesLockedException : NzbDroneException
    {
        public MetadataSourcesLockedException()
            : base("Metadata sources are locked and cannot be changed")
        {
        }
    }

    public interface IMetadataSourceService
    {
        MetadataSources Get();
        void Choose(SceneMetadataType scene, MovieMetadataType movie);
        void EnsureLocked();
    }

    public class MetadataSourceService : IMetadataSourceService
    {
        private readonly IConfigService _configService;
        private readonly object _lock = new();

        public MetadataSourceService(IConfigService configService)
        {
            _configService = configService;
        }

        public MetadataSources Get()
        {
            return new MetadataSources
            {
                Scene = _configService.WhisparrSceneMetadataSource,
                Movie = _configService.WhisparrMovieMetadataSource,
                Locked = _configService.MetadataSourcesLocked
            };
        }

        public void Choose(SceneMetadataType scene, MovieMetadataType movie)
        {
            if (movie == MovieMetadataType.NONE)
            {
                throw new ArgumentException("A movie metadata source is required", nameof(movie));
            }

            lock (_lock)
            {
                if (_configService.MetadataSourcesLocked)
                {
                    throw new MetadataSourcesLockedException();
                }

                _configService.LockMetadataSources(scene, movie);
            }
        }

        public void EnsureLocked()
        {
            lock (_lock)
            {
                if (_configService.MetadataSourcesLocked)
                {
                    return;
                }

                _configService.LockMetadataSources(_configService.WhisparrSceneMetadataSource, _configService.WhisparrMovieMetadataSource);
            }
        }
    }
}
