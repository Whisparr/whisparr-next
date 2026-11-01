using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.RootFolders;
using Whisparr.Api.V5.UnmappedFiles;

namespace NzbDrone.Api.Test.v5.UnmappedFiles
{
    [TestFixture]
    public class UnmappedFileResourceMapperFixture
    {
        private static ImportFile File(bool sameStudio, bool sameDate)
        {
            return new ImportFile
            {
                Id = 4,
                RootFolderId = 2,
                Path = "/media/a.mp4",
                Name = "a.mp4",
                TopMatch = new UnmappedFileMatch
                {
                    ForeignId = "scene",
                    Title = "Title",
                    StudioTitle = "Studio",
                    ReleaseDate = new DateTime(2024, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                    HashMatch = true,
                    SameStudio = sameStudio,
                    SameDate = sameDate
                }
            };
        }

        [Test]
        public void should_map_the_match_when_studio_and_date_both_match()
        {
            var resource = File(true, true).ToResource(true);

            resource.Id.Should().Be(4);
            resource.RootFolderId.Should().Be(2);
            resource.Path.Should().Be("/media/a.mp4");
            resource.Exists.Should().BeTrue();
            resource.Match.Should().NotBeNull();
            resource.Match.ForeignId.Should().Be("scene");
            resource.Match.Title.Should().Be("Title");
            resource.Match.StudioTitle.Should().Be("Studio");
            resource.Match.ReleaseDate.Should().Be(new DateTime(2024, 3, 5, 0, 0, 0, DateTimeKind.Utc));
            resource.Match.HashMatch.Should().BeTrue();
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void should_not_map_the_match_unless_both_studio_and_date_match(bool sameStudio, bool sameDate)
        {
            File(sameStudio, sameDate).ToResource(true).Match.Should().BeNull();
        }

        [Test]
        public void should_not_map_a_match_when_none_is_stored()
        {
            var file = File(true, true);
            file.TopMatch = null;

            file.ToResource(false).Match.Should().BeNull();
        }
    }
}
