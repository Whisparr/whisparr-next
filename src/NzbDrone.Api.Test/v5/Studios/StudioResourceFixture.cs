using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Movies.Studios;
using Whisparr.Api.V5.Studios;

namespace NzbDrone.Api.Test.v5.Studios
{
    [TestFixture]
    public class StudioResourceFixture
    {
        [Test]
        public void should_leave_out_the_images_of_a_studio_without_downloads()
        {
            var model = new Studio
            {
                Id = 1,
                ForeignId = "studio-foreign-id",
                Title = "Some Studio",
                DownloadCovers = false,
                Images = new List<MediaCover> { new(MediaCoverTypes.Poster, "https://example.com/poster.jpg") }
            };

            model.ToResource().Images.Should().BeEmpty();
            model.Images.Should().HaveCount(1);
        }

        [Test]
        public void should_keep_the_images_of_a_studio_with_downloads()
        {
            var model = new Studio
            {
                Id = 1,
                ForeignId = "studio-foreign-id",
                Title = "Some Studio",
                DownloadCovers = true,
                Images = new List<MediaCover> { new(MediaCoverTypes.Poster, "https://example.com/poster.jpg") }
            };

            model.ToResource().Images.Should().HaveCount(1);
        }
    }
}
