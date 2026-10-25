using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore
{
    [TestFixture]
    public class CommandProgressMappingFixture : DbTest<CommandRepository, CommandModel>
    {
        [Test]
        public void should_save_and_load_command_without_progress_column()
        {
            var command = new CommandModel
            {
                Name = "RefreshMonitoredDownloads",
                Body = new RefreshMonitoredDownloadsCommand(),
                Priority = CommandPriority.Normal,
                Status = CommandStatus.Queued,
                QueuedAt = DateTime.UtcNow,
                Progress = new CommandProgress { Phase = "Linking files", Done = 1, Total = 10, Percent = 10 }
            };

            Subject.Insert(command);

            var loaded = Subject.Get(command.Id);

            loaded.Should().NotBeNull();
            loaded.Progress.Should().BeNull();
        }
    }
}
