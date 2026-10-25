using System;
using System.Collections.Concurrent;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ProgressMessaging
{
    public interface IReportCommandProgress
    {
        void Report(string phase, long done, long total, double percent);
    }

    public class CommandProgressReporter : IReportCommandProgress, IHandle<CommandExecutedEvent>
    {
        private static readonly TimeSpan MinPublishInterval = TimeSpan.FromSeconds(1);

        private readonly IEventAggregator _eventAggregator;
        private readonly ConcurrentDictionary<int, DateTime> _lastPublished = new();

        public CommandProgressReporter(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            UtcNow = () => DateTime.UtcNow;
        }

        // Replaceable so tests can drive the throttle
        internal Func<DateTime> UtcNow { get; set; }

        public void Report(string phase, long done, long total, double percent)
        {
            var command = ProgressMessageContext.CommandModel;

            if (command?.Body == null || !command.Body.SendUpdatesToClient)
            {
                return;
            }

            var previousPhase = command.Progress?.Phase;

            var progress = new CommandProgress
            {
                Phase = phase,
                Done = done,
                Total = total,
                Percent = double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 100)
            };

            command.Progress = progress;

            var now = UtcNow();
            var finished = total > 0 && done >= total;

            var publish = !_lastPublished.TryGetValue(command.Id, out var last) ||
                          finished ||
                          previousPhase != phase ||
                          now - last >= MinPublishInterval;

            if (finished)
            {
                _lastPublished.TryRemove(command.Id, out _);
            }
            else if (publish)
            {
                _lastPublished[command.Id] = now;
            }

            if (publish)
            {
                _eventAggregator.PublishEvent(new CommandUpdatedEvent(command));
            }
        }

        // Failed or cancelled commands never report done == total, so drop their throttle entry here
        public void Handle(CommandExecutedEvent message)
        {
            _lastPublished.TryRemove(message.Command.Id, out _);
        }
    }
}
