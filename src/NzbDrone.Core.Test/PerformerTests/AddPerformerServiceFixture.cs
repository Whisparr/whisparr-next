using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Movies.Performers;
using NzbDrone.Core.Movies.Performers.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.PerformerTests
{
    [TestFixture]
    public class AddPerformerServiceFixture : CoreTest<AddPerformerService>
    {
        private const string ForeignId = "performer-foreign-id";

        private Performer _existing;

        [SetUp]
        public void Setup()
        {
            _existing = new Performer
            {
                Id = 12,
                ForeignId = ForeignId,
                Name = "Co Star",
                Visibility = PerformerVisibility.Hidden,
                QualityProfileId = 1,
                RootFolderPath = "/old",
                Tags = new HashSet<int>()
            };

            Mocker.GetMock<IPerformerService>()
                  .Setup(s => s.Update(It.IsAny<Performer>()))
                  .Returns<Performer>(p => p);

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetPerformerInfo(ForeignId))
                  .Returns(new Performer { ForeignId = ForeignId, Name = "Co Star" });
        }

        [Test]
        public void should_show_a_hidden_performer_instead_of_adding_a_duplicate()
        {
            GivenExisting();

            var result = Subject.AddPerformer(GivenNewPerformer(true));

            result.Id.Should().Be(12);
            result.Visibility.Should().Be(PerformerVisibility.Visible);
            result.Monitored.Should().BeTrue();
            result.QualityProfileId.Should().Be(3);
            result.RootFolderPath.Should().Be("/new");
            result.Tags.Should().BeEquivalentTo(new[] { 4 });

            Mocker.GetMock<IPerformerService>().Verify(v => v.Update(_existing), Times.Once());
            Mocker.GetMock<IPerformerService>().Verify(v => v.AddPerformer(It.IsAny<Performer>()), Times.Never());
            Mocker.GetMock<IProvideMovieInfo>().Verify(v => v.GetPerformerInfo(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_refresh_a_hidden_performer_shown_as_monitored()
        {
            GivenExisting();

            Subject.AddPerformer(GivenNewPerformer(true));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Push(It.Is<RefreshPerformersCommand>(c => c.PerformerIds.Contains(12)),
                                      It.IsAny<CommandPriority>(),
                                      It.IsAny<CommandTrigger>()),
                          Times.Once());
        }

        [Test]
        public void should_not_refresh_a_hidden_performer_shown_as_unmonitored()
        {
            GivenExisting();

            var result = Subject.AddPerformer(GivenNewPerformer(false));

            result.Visibility.Should().Be(PerformerVisibility.Visible);

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(v => v.Push(It.IsAny<RefreshPerformersCommand>(),
                                      It.IsAny<CommandPriority>(),
                                      It.IsAny<CommandTrigger>()),
                          Times.Never());
        }

        [Test]
        public void should_add_a_performer_that_is_not_in_the_library()
        {
            Subject.AddPerformer(GivenNewPerformer(true));

            Mocker.GetMock<IPerformerService>()
                  .Verify(v => v.AddPerformer(It.Is<Performer>(p => p.ForeignId == ForeignId &&
                                                                     p.Visibility == PerformerVisibility.Visible)),
                          Times.Once());
            Mocker.GetMock<IPerformerService>().Verify(v => v.Update(It.IsAny<Performer>()), Times.Never());
        }

        [Test]
        public void should_lock_metadata_sources_once_when_adding_a_performer()
        {
            Subject.AddPerformer(GivenNewPerformer(true));

            Mocker.GetMock<IMetadataSourceService>().Verify(v => v.EnsureLocked(), Times.Once());
        }

        [Test]
        public void should_lock_metadata_sources_once_per_batch()
        {
            Mocker.GetMock<IPerformerService>()
                  .Setup(s => s.AllPerformerForeignIds())
                  .Returns(new List<string>());

            Subject.AddPerformers(new List<Performer> { GivenNewPerformer(true), GivenNewPerformer(true) });

            Mocker.GetMock<IMetadataSourceService>().Verify(v => v.EnsureLocked(), Times.Once());
        }

        [Test]
        public void should_warn_not_error_when_a_credit_is_missing_from_the_metadata_source()
        {
            const string missingId = "missing-performer";

            Mocker.GetMock<IPerformerService>()
                  .Setup(s => s.AllPerformerForeignIds())
                  .Returns(new List<string>());

            Mocker.GetMock<IProvideMovieInfo>()
                  .Setup(s => s.GetPerformerInfo(missingId))
                  .Throws(new MovieNotFoundException("Performer not found"));

            Subject.AddPerformers(new List<Performer> { new Performer { ForeignId = missingId } }, true);

            Mocker.GetMock<IPerformerService>()
                  .Verify(v => v.AddPerformers(It.Is<List<Performer>>(l => l.Count == 0)), Times.Once());

            // One for the lookup miss, one for the skipped credit; no errors.
            ExceptionVerification.ExpectedWarns(2);
        }

        private static Performer GivenNewPerformer(bool monitored)
        {
            return new Performer
            {
                ForeignId = ForeignId,
                Monitored = monitored,
                QualityProfileId = 3,
                RootFolderPath = "/new",
                Tags = new HashSet<int> { 4 }
            };
        }

        private void GivenExisting()
        {
            Mocker.GetMock<IPerformerService>()
                  .Setup(s => s.FindByForeignId(ForeignId))
                  .Returns(_existing);
        }
    }
}
