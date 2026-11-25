using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport
{
    [TestFixture]
    public class RejectedFileExtensionServiceFixture : CoreTest<RejectedFileExtensionService>
    {
        [Test]
        public void should_not_return_rejection_for_media_files()
        {
            Subject.GetRejection(new[] { @"C:\Test\Droned.1998.mkv", @"C:\Test\Droned.1998.srt" }).Should().BeNull();
        }

        [Test]
        public void should_not_return_rejection_for_files_without_an_extension()
        {
            Subject.GetRejection(new[] { @"C:\Test\Droned.1998" }).Should().BeNull();
        }

        [Test]
        public void should_return_rejection_for_dangerous_file()
        {
            var rejection = Subject.GetRejection(new[] { @"C:\Test\Droned.1998.mkv", @"C:\Test\Droned.1998.lnk" });

            rejection.Should().NotBeNull();
            rejection.Reason.Should().Be(ImportRejectionReason.DangerousFile);
            rejection.Message.Should().Contain(".lnk");
        }

        [Test]
        public void should_return_rejection_for_executable_file()
        {
            var rejection = Subject.GetRejection(new[] { "Droned.1998/Droned.1998.bat" });

            rejection.Should().NotBeNull();
            rejection.Reason.Should().Be(ImportRejectionReason.ExecutableFile);
            rejection.Message.Should().Contain(".bat");
        }
    }
}
