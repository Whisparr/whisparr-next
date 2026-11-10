using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.SignalR;
using Whisparr.Http.REST;

namespace Whisparr.Http
{
    public interface IBroadcastResourceIds
    {
        void Queue(string name, ModelAction action, IEnumerable<int> ids);
        void Flush();
    }

    public class IdResource : RestResource
    {
    }

    /// <summary>
    /// Collapses per-entity change events into one id-only SignalR message per (resource, action),
    /// flushed shortly after the first id arrives, so bulk work doesn't broadcast once per entity.
    /// </summary>
    public class BroadcastResourceIds : IBroadcastResourceIds
    {
        private const int FlushIntervalMs = 1000;

        private readonly IBroadcastSignalRMessage _signalRBroadcaster;
        private readonly Timer _timer;
        private readonly object _lock = new();
        private Dictionary<(string Name, ModelAction Action), HashSet<int>> _pending = new();
        private bool _armed;

        public BroadcastResourceIds(IBroadcastSignalRMessage signalRBroadcaster)
        {
            _signalRBroadcaster = signalRBroadcaster;
            _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Queue(string name, ModelAction action, IEnumerable<int> ids)
        {
            lock (_lock)
            {
                var key = (name, action);

                if (!_pending.TryGetValue(key, out var set))
                {
                    _pending[key] = set = new HashSet<int>();
                }

                set.UnionWith(ids);

                if (!_armed)
                {
                    _armed = true;
                    _timer.Change(FlushIntervalMs, Timeout.Infinite);
                }
            }
        }

        public void Flush()
        {
            Dictionary<(string Name, ModelAction Action), HashSet<int>> batch;

            lock (_lock)
            {
                batch = _pending;
                _pending = new();
                _armed = false;
            }

            if (!_signalRBroadcaster.IsConnected)
            {
                return;
            }

            foreach (var ((name, action), ids) in batch)
            {
                _signalRBroadcaster.BroadcastMessage(new SignalRMessage
                {
                    Name = name,
                    Body = new ResourceChangeMessage<IdResource>(ids.Select(id => new IdResource { Id = id }), action),
                    Version = 5,
                    Action = action
                });
            }
        }
    }
}
