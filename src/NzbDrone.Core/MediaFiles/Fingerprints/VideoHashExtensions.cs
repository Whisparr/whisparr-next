using System;
using NLog;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.MediaFiles.Fingerprints
{
    public static class VideoHashExtensions
    {
        /// <summary>
        /// Computes the oshash for a file and the last-write time it was hashed at. Never throws:
        /// a hash failure yields a null hash, a vanished file yields a null last-write time.
        /// </summary>
        public static (string OsHash, DateTime? ModifiedAt) ComputeOsHashStamp(this IVideoHashService hashService, IDiskProvider diskProvider, string path, Logger logger)
        {
            var modifiedAt = TryGetLastWrite(diskProvider, path, logger);
            string osHash = null;

            try
            {
                osHash = hashService.ComputeOsHash(path);
            }
            catch (Exception ex)
            {
                // A hashing problem never fails an import or scan
                logger.Debug(ex, "Unable to compute oshash for {0}", path);
            }

            return (osHash, modifiedAt);
        }

        public static DateTime? TryGetLastWrite(IDiskProvider diskProvider, string path, Logger logger)
        {
            try
            {
                return diskProvider.FileGetLastWrite(path);
            }
            catch (Exception ex)
            {
                logger.Debug(ex, "Unable to read last-write time for {0}", path);

                return null;
            }
        }

        /// <summary>
        /// Last-write times lose sub-second precision in DB round-trips, so compare whole seconds in UTC.
        /// </summary>
        public static bool SameLastWrite(DateTime a, DateTime b)
        {
            return TruncateToSecond(a) == TruncateToSecond(b);
        }

        private static DateTime TruncateToSecond(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;

            return new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        }
    }
}
