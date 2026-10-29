using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.LegacyMigration
{
    public interface ILegacyV2SeriesRepository : IBasicRepository<LegacyV2Series>
    {
    }

    public class LegacyV2SeriesRepository : BasicRepository<LegacyV2Series>, ILegacyV2SeriesRepository
    {
        public LegacyV2SeriesRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ILegacyV2EpisodeRepository : IBasicRepository<LegacyV2Episode>
    {
    }

    public class LegacyV2EpisodeRepository : BasicRepository<LegacyV2Episode>, ILegacyV2EpisodeRepository
    {
        public LegacyV2EpisodeRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ILegacyV2EpisodeFileRepository : IBasicRepository<LegacyV2EpisodeFile>
    {
    }

    public class LegacyV2EpisodeFileRepository : BasicRepository<LegacyV2EpisodeFile>, ILegacyV2EpisodeFileRepository
    {
        public LegacyV2EpisodeFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ILegacyMovieSeedRepository : IBasicRepository<LegacyMovieSeed>
    {
    }

    public class LegacyMovieSeedRepository : BasicRepository<LegacyMovieSeed>, ILegacyMovieSeedRepository
    {
        public LegacyMovieSeedRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ILegacyStudioSeedRepository : IBasicRepository<LegacyStudioSeed>
    {
    }

    public class LegacyStudioSeedRepository : BasicRepository<LegacyStudioSeed>, ILegacyStudioSeedRepository
    {
        public LegacyStudioSeedRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ILegacyPerformerSeedRepository : IBasicRepository<LegacyPerformerSeed>
    {
    }

    public class LegacyPerformerSeedRepository : BasicRepository<LegacyPerformerSeed>, ILegacyPerformerSeedRepository
    {
        public LegacyPerformerSeedRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ILegacySceneSeedRepository : IBasicRepository<LegacySceneSeed>
    {
    }

    public class LegacySceneSeedRepository : BasicRepository<LegacySceneSeed>, ILegacySceneSeedRepository
    {
        public LegacySceneSeedRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
