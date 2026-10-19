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
        public bool Stopped { get; set; }
        public List<string> GaveUp { get; set; } = new();
    }

    public interface IScenePageWalker
    {
        // fetchPage(cursor) -> page; handlePage is called for EVERY response that carries items, 200 or 202.
        // keepGoing is asked before each fetch; when it returns false the walk ends cleanly with Stopped set.
        ScenePageWalkResult Walk(Func<string, ScenePage> fetchPage, Action<ScenePage> handlePage, Func<bool> keepGoing);
    }

    public class ScenePageWalker : IScenePageWalker
    {
        public static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(15);

        private readonly ISkyhookPacer _pacer;
        private readonly Logger _logger;

        public ScenePageWalker(ISkyhookPacer pacer, Logger logger)
        {
            _pacer = pacer;
            _logger = logger;
        }

        public ScenePageWalkResult Walk(Func<string, ScenePage> fetchPage, Action<ScenePage> handlePage, Func<bool> keepGoing)
        {
            var result = new ScenePageWalkResult();
            var revisit = new List<(string Cursor, ScenePage Page)>();
            var visited = new HashSet<string> { string.Empty };
            string cursor = null;

            do
            {
                if (!keepGoing())
                {
                    result.Stopped = true;
                    return result;
                }

                var page = FetchUnthrottled(fetchPage, cursor);

                if (page == null)
                {
                    result.GaveUp.Add($"cursor:{cursor}");

                    // The earlier pages still waiting on Skyhook would otherwise be forgotten.
                    foreach (var (_, pending) in revisit)
                    {
                        result.GaveUp.AddRange(pending.Pending);
                    }

                    return result;
                }

                handlePage(page);
                result.Pages++;

                if (page.Status == ScenePageStatus.Pending)
                {
                    revisit.Add((cursor, page));
                }

                cursor = page.NextCursor;

                if (cursor != null && !visited.Add(cursor))
                {
                    _logger.Warn("Skyhook returned scene page cursor '{0}' twice; ending the first pass to avoid looping", cursor);
                    break;
                }
            }
            while (cursor != null);

            foreach (var (pageCursor, pending) in revisit)
            {
                if (!Revisit(fetchPage, handlePage, keepGoing, pageCursor, pending, result))
                {
                    result.Stopped = true;
                    break;
                }
            }

            return result;
        }

        // Returns null when throttling has outlasted StallTimeout or the catalogue is still building after BuildTimeout.
        // A Building page carries no items, so it is waited out in place and never reaches handlePage or the revisit list.
        private ScenePage FetchUnthrottled(Func<string, ScenePage> fetchPage, string cursor)
        {
            var throttledSince = (DateTime?)null;
            var buildingSince = (DateTime?)null;

            while (true)
            {
                var page = fetchPage(cursor);

                if (page.Status == ScenePageStatus.Building)
                {
                    buildingSince ??= _pacer.UtcNow;

                    if (_pacer.UtcNow - buildingSince.Value >= BuildTimeout)
                    {
                        _logger.Warn("Skyhook was still building the catalogue for scene page cursor '{0}' after {1}; giving up until the next sync", cursor, BuildTimeout);
                        return null;
                    }

                    _pacer.Sleep(page.RetryAfter);
                    continue;
                }

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

        // Returns false when keepGoing stopped the walk.
        private bool Revisit(Func<string, ScenePage> fetchPage, Action<ScenePage> handlePage, Func<bool> keepGoing, string cursor, ScenePage last, ScenePageWalkResult result)
        {
            var stalledSince = _pacer.UtcNow;
            var lastPending = last.Pending;
            var pendingCount = lastPending.Count;

            while (true)
            {
                _pacer.Sleep(last.RetryAfter);

                if (!keepGoing())
                {
                    return false;
                }

                var page = fetchPage(cursor);

                if (page.Status != ScenePageStatus.Throttled && page.Status != ScenePageStatus.Building)
                {
                    handlePage(page);

                    if (page.Status == ScenePageStatus.Complete)
                    {
                        return true;
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
                    return true;
                }
            }
        }
    }
}
