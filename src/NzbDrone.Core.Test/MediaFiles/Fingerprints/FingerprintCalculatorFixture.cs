using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.Test.Framework;
using Whisparr.Fingerprints;

namespace NzbDrone.Core.Test.MediaFiles.Fingerprints
{
    [TestFixture]
    public class FingerprintCalculatorFixture : CoreTest<FingerprintCalculator>
    {
        [Test]
        public void OsHash_returns_16_lowercase_hex_for_a_file_over_128KiB()
        {
            var path = Path.GetTempFileName();

            try
            {
                var bytes = new byte[256 * 1024];

                for (var i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = (byte)((i * 31) % 251);
                }

                File.WriteAllBytes(path, bytes);

                var result = Subject.OsHash(path);

                result.Should().MatchRegex("^[0-9a-f]{16}$");
                result.Should().Be(Whisparr.Fingerprints.OsHash.Compute(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void FFmpegAvailable_matches_locator()
        {
            Subject.FFmpegAvailable.Should().Be(FFmpegLocator.IsBundledAvailable);
        }
    }
}
