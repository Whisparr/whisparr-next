using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport.Specifications
{
    public class NotMultiPartSpecification : IImportDecisionEngineSpecification
    {
        private static readonly Regex[] MovieMultiPartRegex = new[]
        {
            new Regex(@"(?<!^)(?<identifier>[ _.-]*(?:cd|dvd|p(?:(?:ar)?t)?|dis[ck])[ _.-]*[0-9]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexDefaults.Timeout),
            new Regex(@"(?<!^)(?<identifier>[ _.-]*(?:cd|dvd|p(?:(?:ar)?t)?|dis[ck])[ _.-]*[a-d]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexDefaults.Timeout),
        };

        private static readonly TimeSpan FolderLifetime = TimeSpan.FromMinutes(1);

        private readonly IDiskProvider _diskProvider;
        private readonly ICached<FolderParts> _folderCache;
        private readonly Logger _logger;

        public NotMultiPartSpecification(IDiskProvider diskProvider, ICacheManager cacheManager, Logger logger)
        {
            _diskProvider = diskProvider;
            _folderCache = cacheManager.GetCache<FolderParts>(GetType(), "folders");
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            if (MovieMultiPartRegex.Any(v => v.IsMatch(localMovie.Path)))
            {
                var parts = GetFolderParts(localMovie.Path);

                for (var i = 0; i < MovieMultiPartRegex.Length; i++)
                {
                    if (parts.Counts[i].GetValueOrDefault(MovieMultiPartRegex[i].Replace(localMovie.Path, "")) > 1)
                    {
                        _logger.Debug("Rejected Multi-Part File: {0}", localMovie.Path);

                        return ImportSpecDecision.Reject(ImportRejectionReason.MultiPartMovie, "File is suspected multi-part file, Whisparr doesn't support this");
                    }
                }
            }

            return ImportSpecDecision.Accept();
        }

        // A flat folder holds the whole library and ".mp4" alone matches the regex, so list and strip each folder once per batch
        private FolderParts GetFolderParts(string path)
        {
            var folder = path.GetParentPath();
            var parts = _folderCache.Get(folder, () => ListFolder(folder), FolderLifetime);

            // A file newer than the cached listing means the folder changed: list it again
            if (!parts.Files.Contains(path))
            {
                parts = ListFolder(folder);
                _folderCache.Set(folder, parts, FolderLifetime);
            }

            return parts;
        }

        private FolderParts ListFolder(string folder)
        {
            var files = _diskProvider.GetFiles(folder, false).ToList();

            return new FolderParts
            {
                Files = files.ToHashSet(),
                Counts = MovieMultiPartRegex.Select(r => files.GroupBy(f => r.Replace(f, "")).ToDictionary(g => g.Key, g => g.Count())).ToArray()
            };
        }

        private class FolderParts
        {
            public HashSet<string> Files { get; init; }
            public Dictionary<string, int>[] Counts { get; init; }
        }
    }
}
