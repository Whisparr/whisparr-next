using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Studios;
using NzbDrone.Core.Movies.Studios.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.StudioTests
{
    [TestFixture]
    public class StudioServiceFixture : CoreTest<StudioService>
    {
        [Test]
        public void should_publish_one_updated_event_per_update()
        {
            var studio = Builder<Studio>.CreateNew().Build();

            Mocker.GetMock<IStudioRepository>()
                .Setup(s => s.Update(studio))
                .Returns(studio);

            Subject.Update(studio);

            // RefreshStudioService relies on this being the only StudioUpdatedEvent
            Mocker.GetMock<IEventAggregator>()
                .Verify(s => s.PublishEvent(It.Is<StudioUpdatedEvent>(e => e.Studio == studio)), Times.Once());
        }

        private Studio GivenStudioTitled(string cleanTitle)
        {
            var studio = new Studio { Id = 7, Title = "Cosmid" };

            Mocker.GetMock<IStudioRepository>()
                .Setup(s => s.AllCleanTitles())
                .Returns(new Dictionary<string, List<int>> { { cleanTitle, new List<int> { studio.Id } } });

            Mocker.GetMock<IStudioRepository>()
                .Setup(s => s.FindByIds(It.Is<List<int>>(ids => ids.SequenceEqual(new[] { studio.Id }))))
                .Returns(new List<Studio> { studio });

            return studio;
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void should_return_empty_for_a_blank_title(string title)
        {
            Subject.FindAllByTitle(title).Should().BeEmpty();

            Mocker.GetMock<IStudioRepository>().Verify(s => s.AllCleanTitles(), Times.Never());
        }

        [Test]
        public void should_find_studios_by_cleaned_title()
        {
            var studio = GivenStudioTitled("cosmid");

            Subject.FindAllByTitle("Cosmid").Should().Equal(studio);
            Subject.FindByTitle("COSMID").Should().Be(studio);
        }

        [Test]
        public void should_return_empty_for_an_unknown_title()
        {
            GivenStudioTitled("cosmid");

            Subject.FindAllByTitle("Transfixed").Should().BeEmpty();
            Subject.FindByTitle("Transfixed").Should().BeNull();
        }

        [Test]
        public void should_build_the_title_lookup_once()
        {
            GivenStudioTitled("cosmid");

            Subject.FindAllByTitle("Cosmid");
            Subject.FindAllByTitle("Transfixed");
            Subject.FindByTitle("Cosmid");

            Mocker.GetMock<IStudioRepository>().Verify(s => s.AllCleanTitles(), Times.Once());
        }

        [Test]
        public void should_rebuild_the_title_lookup_after_studios_change()
        {
            var studio = GivenStudioTitled("cosmid");

            Subject.FindAllByTitle("Cosmid");
            Subject.AddStudio(studio);
            Subject.FindAllByTitle("Cosmid");
            Subject.AddStudios(new List<Studio> { studio });
            Subject.FindAllByTitle("Cosmid");
            Subject.Update(studio);
            Subject.FindAllByTitle("Cosmid");
            Subject.Update(new List<Studio> { studio });
            Subject.FindAllByTitle("Cosmid");
            Subject.RemoveStudio(studio);
            Subject.FindAllByTitle("Cosmid");

            Mocker.GetMock<IStudioRepository>().Verify(s => s.AllCleanTitles(), Times.Exactly(6));
        }
    }
}
