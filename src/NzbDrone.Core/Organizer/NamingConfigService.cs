using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Organizer
{
    public interface INamingConfigService
    {
        NamingConfig GetConfig();
        void Save(NamingConfig namingConfig);
    }

    public class NamingConfigService : INamingConfigService
    {
        private readonly INamingConfigRepository _repository;

        private readonly IEventAggregator _eventAggregator;

        public NamingConfigService(INamingConfigRepository repository, IEventAggregator eventAggregator)
        {
            _repository = repository;
            _eventAggregator = eventAggregator;
        }

        public NamingConfig GetConfig()
        {
            var config = _repository.SingleOrDefault();

            if (config == null)
            {
                lock (_repository)
                {
                    config = _repository.SingleOrDefault();

                    if (config == null)
                    {
                        _repository.Insert(NamingConfig.Default);
                        config = _repository.Single();
                    }
                }
            }

            return config;
        }

        public void Save(NamingConfig namingConfig)
        {
            _repository.Upsert(namingConfig);
            _eventAggregator.PublishEvent(new NamingConfigSavedEvent());
        }
    }
}
