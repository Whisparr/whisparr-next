using System;
using System.Threading;
using NLog;
using NzbDrone.Core.MediaFiles.MediaInfo;

namespace NzbDrone.Core.MediaFiles.Fingerprints
{
    public interface IVideoHashService
    {
        string ComputeOsHash(string path);
        string ComputePHash(string path);
        bool CanComputePHash { get; }
    }

    public class VideoHashService : IVideoHashService
    {
        private readonly IFingerprintCalculator _calculator;
        private readonly IVideoFileInfoReader _videoFileInfoReader;
        private readonly Logger _logger;

        public VideoHashService(IFingerprintCalculator calculator, IVideoFileInfoReader videoFileInfoReader, Logger logger)
        {
            _calculator = calculator;
            _videoFileInfoReader = videoFileInfoReader;
            _logger = logger;
        }

        internal TimeSpan PHashTimeout { get; set; } = TimeSpan.FromMinutes(10);

        public bool CanComputePHash => _calculator.FFmpegAvailable;

        public string ComputeOsHash(string path)
        {
            try
            {
                return _calculator.OsHash(path);
            }
            catch (Exception ex)
            {
                _logger.Warn("oshash failed for {0}: {1}", path, ex.Message);
                return null;
            }
        }

        public string ComputePHash(string path)
        {
            try
            {
                var duration = _videoFileInfoReader.GetContainerDuration(path);

                if (duration is not > 0)
                {
                    _logger.Warn("phash skipped for {0}: no usable container duration", path);
                    return null;
                }

                using var cts = new CancellationTokenSource(PHashTimeout);

                return _calculator.PHashAsync(path, duration.Value, cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                _logger.Warn("phash timed out for {0}", path);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warn("phash failed for {0}: {1}", path, ex.Message);
                return null;
            }
        }
    }
}
