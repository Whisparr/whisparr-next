using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Test.Common;
using Whisparr.Api.V5.System.MigrationReport;

namespace NzbDrone.Api.Test.v5
{
    [TestFixture]
    public class MigrationReportControllerFixture : TestBase<MigrationReportController>
    {
        [Test]
        public void get_returns_404_without_state()
        {
            Mocker.GetMock<ILegacyMigrationService>()
                .Setup(s => s.GetCurrent())
                .Returns((LegacyMigrationState)null);

            Subject.GetReport().Result.Should().BeOfType<NotFoundResult>();
        }

        [Test]
        public void get_maps_state_and_items_ordered()
        {
            var started = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            var completed = started.AddMinutes(5);

            Mocker.GetMock<ILegacyMigrationService>()
                .Setup(s => s.GetCurrent())
                .Returns(new LegacyMigrationState
                {
                    SourceKind = LegacyKind.V2,
                    SceneSource = SceneMetadataType.Tpdb,
                    MovieSource = MovieMetadataType.TPDB,
                    Completed = true,
                    StartedAt = started,
                    CompletedAt = completed,
                    BackupPath = "/backups/pre-migration.zip",
                    ReportDismissed = true
                });

            Mocker.GetMock<ILegacyMigrationService>()
                .Setup(s => s.GetReport())
                .Returns(new List<LegacyMigrationReportItem>
                {
                    new() { Id = 7, Category = LegacyReportCategory.NeedsAttention, Area = "Files", Subject = "a", Detail = "d1", Count = 1 },
                    new() { Category = LegacyReportCategory.SkippedSetting, Area = "Naming", Subject = "z", Detail = "d2", Count = 2 },
                    new() { Category = LegacyReportCategory.SkippedSetting, Area = "Naming", Subject = "b", Detail = "d3", Count = 3 },
                    new() { Category = LegacyReportCategory.SkippedSetting, Area = "Indexers", Subject = "m", Detail = "d4", Count = 4 }
                });

            var resource = Subject.GetReport().Value;

            resource.SourceKind.Should().Be("V2");
            resource.SceneSource.Should().Be("Tpdb");
            resource.MovieSource.Should().Be("TPDB");
            resource.Completed.Should().BeTrue();
            resource.StartedAt.Should().Be(started);
            resource.CompletedAt.Should().Be(completed);
            resource.BackupPath.Should().Be("/backups/pre-migration.zip");
            resource.Dismissed.Should().BeTrue();

            resource.Items.Select(i => i.Subject).Should().Equal("m", "b", "z", "a");
            resource.Items[0].Category.Should().Be("SkippedSetting");
            resource.Items[0].Detail.Should().Be("d4");
            resource.Items[0].Count.Should().Be(4);
            resource.Items[3].Id.Should().Be(7);
        }

        [Test]
        public void dismiss_calls_service()
        {
            Mocker.GetMock<ILegacyMigrationService>()
                .Setup(s => s.GetCurrent())
                .Returns(new LegacyMigrationState { Completed = true });

            Subject.Dismiss().Should().BeOfType<OkResult>();

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.DismissReport(), Times.Once());
        }

        [Test]
        public void dismiss_returns_409_until_the_migration_has_completed()
        {
            Mocker.GetMock<ILegacyMigrationService>()
                .Setup(s => s.GetCurrent())
                .Returns(new LegacyMigrationState { Completed = false });

            Subject.Dismiss().Should().BeOfType<ConflictResult>();

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.DismissReport(), Times.Never());
        }

        [Test]
        public void remove_item_calls_service()
        {
            Mocker.GetMock<ILegacyMigrationService>()
                .Setup(s => s.GetCurrent())
                .Returns(new LegacyMigrationState { Completed = true });

            Subject.RemoveItem(5).Should().BeOfType<OkResult>();

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.RemoveReportItem(5), Times.Once());
        }

        [Test]
        public void remove_item_returns_409_until_the_migration_has_completed()
        {
            Mocker.GetMock<ILegacyMigrationService>()
                .Setup(s => s.GetCurrent())
                .Returns(new LegacyMigrationState { Completed = false });

            Subject.RemoveItem(5).Should().BeOfType<ConflictResult>();

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.RemoveReportItem(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void remove_item_returns_404_without_state()
        {
            Mocker.GetMock<ILegacyMigrationService>()
                .Setup(s => s.GetCurrent())
                .Returns((LegacyMigrationState)null);

            Subject.RemoveItem(5).Should().BeOfType<NotFoundResult>();

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.RemoveReportItem(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void dismiss_returns_404_without_state()
        {
            Mocker.GetMock<ILegacyMigrationService>()
                .Setup(s => s.GetCurrent())
                .Returns((LegacyMigrationState)null);

            Subject.Dismiss().Should().BeOfType<NotFoundResult>();

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.DismissReport(), Times.Never());
        }
    }
}
