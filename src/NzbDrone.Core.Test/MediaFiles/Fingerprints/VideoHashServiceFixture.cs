using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.Fingerprints;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Test.Framework;
using Whisparr.Fingerprints;

namespace NzbDrone.Core.Test.MediaFiles.Fingerprints
{
    [TestFixture]
    public class VideoHashServiceFixture : CoreTest<VideoHashService>
    {
        private const string Path = "/library/scene.mp4";

        [Test]
        public void ComputeOsHash_returns_calculator_value()
        {
            Mocker.GetMock<IFingerprintCalculator>()
                .Setup(s => s.OsHash(Path))
                .Returns("0123456789abcdef");

            Subject.ComputeOsHash(Path).Should().Be("0123456789abcdef");
        }

        [TestCase(typeof(IOException))]
        [TestCase(typeof(UnauthorizedAccessException))]
        [TestCase(typeof(FileNotFoundException))]
        [TestCase(typeof(ArgumentException))]
        public void ComputeOsHash_returns_null_when_calculator_throws(Type exceptionType)
        {
            Mocker.GetMock<IFingerprintCalculator>()
                .Setup(s => s.OsHash(Path))
                .Throws((Exception)Activator.CreateInstance(exceptionType));

            Subject.ComputeOsHash(Path).Should().BeNull();
        }

        [Test]
        public void ComputePHash_passes_rounded_container_duration()
        {
            Mocker.GetMock<IVideoFileInfoReader>()
                .Setup(s => s.GetContainerDuration(Path))
                .Returns(1234.57);

            Mocker.GetMock<IFingerprintCalculator>()
                .Setup(s => s.PHashAsync(Path, 1234.57, It.IsAny<CancellationToken>()))
                .ReturnsAsync("abcdef");

            Subject.ComputePHash(Path).Should().Be("abcdef");

            Mocker.GetMock<IFingerprintCalculator>()
                .Verify(s => s.PHashAsync(Path, 1234.57, It.IsAny<CancellationToken>()), Times.Once());
        }

        [TestCase(null)]
        [TestCase(0.0)]
        [TestCase(-5.0)]
        public void ComputePHash_returns_null_without_calling_calculator_when_duration_is_null_or_not_positive(double? duration)
        {
            Mocker.GetMock<IVideoFileInfoReader>()
                .Setup(s => s.GetContainerDuration(Path))
                .Returns(duration);

            Subject.ComputePHash(Path).Should().BeNull();

            Mocker.GetMock<IFingerprintCalculator>()
                .Verify(s => s.PHashAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()), Times.Never());
        }

        [Test]
        public void ComputePHash_returns_null_when_calculator_throws()
        {
            Mocker.GetMock<IVideoFileInfoReader>()
                .Setup(s => s.GetContainerDuration(Path))
                .Returns(60.0);

            Mocker.GetMock<IFingerprintCalculator>()
                .Setup(s => s.PHashAsync(Path, 60.0, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new FFmpegException("boom"));

            Subject.ComputePHash(Path).Should().BeNull();
        }

        [Test]
        public void ComputePHash_returns_null_when_calculator_times_out()
        {
            Mocker.GetMock<IVideoFileInfoReader>()
                .Setup(s => s.GetContainerDuration(Path))
                .Returns(60.0);

            Mocker.GetMock<IFingerprintCalculator>()
                .Setup(s => s.PHashAsync(Path, 60.0, It.IsAny<CancellationToken>()))
                .Returns<string, double, CancellationToken>(async (_, _, token) =>
                {
                    await Task.Delay(Timeout.Infinite, token);
                    return "never";
                });

            Subject.PHashTimeout = TimeSpan.FromMilliseconds(100);

            Subject.ComputePHash(Path).Should().BeNull();
        }

        [Test]
        public void ComputePHash_returns_null_when_duration_probe_throws()
        {
            Mocker.GetMock<IVideoFileInfoReader>()
                .Setup(s => s.GetContainerDuration(Path))
                .Throws(new IOException("probe"));

            Subject.ComputePHash(Path).Should().BeNull();
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CanComputePHash_reflects_calculator(bool available)
        {
            Mocker.GetMock<IFingerprintCalculator>()
                .SetupGet(s => s.FFmpegAvailable)
                .Returns(available);

            Subject.CanComputePHash.Should().Be(available);
        }
    }
}
