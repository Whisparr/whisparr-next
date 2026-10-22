using System.Threading;
using System.Threading.Tasks;
using Whisparr.Fingerprints;

namespace NzbDrone.Core.MediaFiles.Fingerprints
{
    public interface IFingerprintCalculator
    {
        bool FFmpegAvailable { get; }
        string OsHash(string path);
        Task<string> PHashAsync(string path, double durationSeconds, CancellationToken cancellationToken);
    }

    public class FingerprintCalculator : IFingerprintCalculator
    {
        public bool FFmpegAvailable => FFmpegLocator.IsBundledAvailable;

        public string OsHash(string path)
        {
            return Whisparr.Fingerprints.OsHash.Compute(path);
        }

        public Task<string> PHashAsync(string path, double durationSeconds, CancellationToken cancellationToken)
        {
            return PerceptualHash.ComputeAsync(path, durationSeconds, null, cancellationToken);
        }
    }
}
