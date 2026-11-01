using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.RootFolders.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.JobTests
{
    [TestFixture]
    public class TaskManagerFixture : CoreTest<TaskManager>
    {
        [Test]
        public void should_not_schedule_finding_matches_for_unmapped_files()
        {
            var scheduled = new List<string>();

            Mocker.GetMock<IScheduledTaskRepository>().Setup(r => r.All()).Returns(new List<ScheduledTask>());
            Mocker.GetMock<IScheduledTaskRepository>()
                  .Setup(r => r.Upsert(It.IsAny<ScheduledTask>()))
                  .Callback((ScheduledTask t) => scheduled.Add(t.TypeName))
                  .Returns((ScheduledTask t) => t);

            Subject.Handle(new ApplicationStartedEvent());

            Assert.That(scheduled, Is.Not.Empty);
            Assert.That(scheduled, Does.Not.Contain(typeof(FindUnmappedFileMatchesCommand).FullName));
        }
    }
}
