using System;
using System.Collections.Generic;
using System.Threading;
using NLog;

namespace NzbDrone.Core.MetadataSource
{
    public interface ISkyhookPacer
    {
        DateTime UtcNow { get; }
        void Sleep(TimeSpan duration);
    }

    public class SkyhookPacer : ISkyhookPacer
    {
        public DateTime UtcNow => DateTime.UtcNow;

        public void Sleep(TimeSpan duration)
        {
            Thread.Sleep(duration);
        }
    }

    public class ScenePageWalkResult
    {
        public int Pages { get; set; }
        public List<string> GaveUp { get; set; } = new();
    }

    public interface IScenePageWalker
    {
        // fetchPage(cursor) -> page; handlePage is called for EVERY response that carries items, 200 or 202
        ScenePageWalkResult Walk(Func<string, ScenePage> fetchPage, Action<ScenePage> handlePage);
    }

    public class ScenePageWalker : IScenePageWalker
    {
        public static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(5);

        private readonly ISkyhookPacer _pacer;
        private readonly Logger _logger;

        public ScenePageWalker(ISkyhookPacer pacer, Logger logger)
        {
            _pacer = pacer;
            _logger = logger;
        }

        public ScenePageWalkResult Walk(Func<string, ScenePage> fetchPage, Action<ScenePage> handlePage)
        {
            var result = new ScenePageWalkResult();
            var revisit = new List<(string Cursor, ScenePage Page)>();
            string cursor = null;

            do
            {
                var page = FetchUnthrottled(fetchPage, cursor);

                if (page == null)
                {
                    result.GaveUp.Add($"cursor:{cursor}");
                    return result;
                }

                handlePage(page);
                result.Pages++;

                if (page.Status == ScenePageStatus.Pending)
                {
                    revisit.Add((cursor, page));
                }

                cursor = page.NextCursor;
            }
            while (cursor != null);

            foreach (var (pageCursor, pending) in revisit)
            {
                Revisit(fetchPage, handlePage, pageCursor, pending, result);
            }

            return result;
        }

        // Returns null when throttling has outlasted StallTimeout.
        private ScenePage FetchUnthrottled(Func<string, ScenePage> fetchPage, string cursor)
        {
            var throttledSince = (DateTime?)null;

            while (true)
            {
                var page = fetchPage(cursor);

                if (page.Status != ScenePageStatus.Throttled)
                {
                    return page;
                }

                throttledSince ??= _pacer.UtcNow;

                if (_pacer.UtcNow - throttledSince.Value >= StallTimeout)
                {
                    _logger.Warn("Skyhook kept throttling scene page cursor '{0}' for {1}; giving up until the next sync", cursor, StallTimeout);
                    return null;
                }

                _pacer.Sleep(page.RetryAfter);
            }
        }

        private void Revisit(Func<string, ScenePage> fetchPage, Action<ScenePage> handlePage, string cursor, ScenePage last, ScenePageWalkResult result)
        {
            var stalledSince = _pacer.UtcNow;
            var lastPending = last.Pending;
            var pendingCount = lastPending.Count;

            while (true)
            {
                _pacer.Sleep(last.RetryAfter);

                var page = fetchPage(cursor);

                if (page.Status != ScenePageStatus.Throttled)
                {
                    handlePage(page);

                    if (page.Status == ScenePageStatus.Complete)
                    {
                        return;
                    }

                    if (page.Pending.Count < pendingCount)
                    {
                        stalledSince = _pacer.UtcNow;
                    }

                    pendingCount = page.Pending.Count;
                    lastPending = page.Pending;
                }

                last = page;

                if (_pacer.UtcNow - stalledSince >= StallTimeout)
                {
                    _logger.Warn("Skyhook scene page cursor '{0}' made no progress for {1}; giving up on {2} scene(s): {3}", cursor, StallTimeout, lastPending.Count, string.Join(", ", lastPending));
                    result.GaveUp.AddRange(lastPending);
                    return;
                }
            }
        }
    }
}
