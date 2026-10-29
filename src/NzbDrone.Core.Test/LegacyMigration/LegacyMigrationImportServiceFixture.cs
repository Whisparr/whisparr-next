using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class LegacyMigrationImportServiceFixture : CoreTest<LegacyMigrationImportService>
    {
        private LegacyMigrationState _state;
        private Mock<ILegacyImportRunner> _runner;
        private Queue<LegacyImportPass> _passes;
        private Action<ILegacyImportProgress> _duringPass;

        [SetUp]
        public void Setup()
        {
            _state = new LegacyMigrationState { Id = 1, Completed = false };
            _passes = new Queue<LegacyImportPass>();
            _duringPass = null;

            Mocker.GetMock<ILegacyMigrationService>()
                  .Setup(s => s.GetCurrent())
                  .Returns(() => _state);

            _runner = new Mock<ILegacyImportRunner>();
            _runner.Setup(r => r.Handles(It.IsAny<LegacyMigrationState>())).Returns(true);
            _runner.Setup(r => r.RunPass(It.IsAny<LegacyMigrationState>(), It.IsAny<ILegacyImportProgress>()))
                   .Returns((LegacyMigrationState s, ILegacyImportProgress p) =>
                   {
                       _duringPass?.Invoke(p);

                       return _passes.Dequeue();
                   });

            Mocker.SetConstant<IEnumerable<ILegacyImportRunner>>(new[] { _runner.Object });
        }

        private static LegacyImportPass Pass(int pending, int waiting = 0, int nextAttempt = 0, int resolving = 0)
        {
            return new LegacyImportPass { Pending = pending, WaitingRetry = waiting, NextAttempt = nextAttempt, Waiting = resolving };
        }

        [Test]
        public void queues_command_on_startup_when_incomplete()
        {
            Mocker.GetMock<ILegacyMigrationService>().Setup(s => s.IsIncomplete()).Returns(true);

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.IsAny<LegacyMigrationImportCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void does_not_queue_when_completed_or_absent()
        {
            Mocker.GetMock<ILegacyMigrationService>().Setup(s => s.IsIncomplete()).Returns(false);

            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.IsAny<LegacyMigrationImportCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void returns_without_running_when_state_absent_or_completed()
        {
            _state = null;
            Subject.Execute(new LegacyMigrationImportCommand());

            _state = new LegacyMigrationState { Id = 1, Completed = true };
            Subject.Execute(new LegacyMigrationImportCommand());

            _runner.Verify(r => r.RunPass(It.IsAny<LegacyMigrationState>(), It.IsAny<ILegacyImportProgress>()), Times.Never());
        }

        [Test]
        public void completes_when_runner_reports_nothing_pending()
        {
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.MarkCompleted(), Times.Once());
        }

        [Test]
        public void sleeps_policy_delay_then_retries_waiting_items()
        {
            _passes.Enqueue(Pass(2, 2, 1));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.FromMinutes(1)), Times.Once());
            _runner.Verify(r => r.RunPass(It.IsAny<LegacyMigrationState>(), It.IsAny<ILegacyImportProgress>()), Times.Exactly(2));
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.MarkCompleted(), Times.Once());
        }

        [Test]
        public void sleeps_the_delay_for_the_smallest_waiting_attempt()
        {
            _passes.Enqueue(Pass(1, 1, 3));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.FromMinutes(4)), Times.Once());
        }

        [Test]
        public void sleeps_the_pending_delay_while_skyhook_is_resolving()
        {
            _passes.Enqueue(Pass(2, resolving: 2));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(LegacyRetryPolicy.PendingDelay), Times.Once());
            _runner.Verify(r => r.RunPass(It.IsAny<LegacyMigrationState>(), It.IsAny<ILegacyImportProgress>()), Times.Exactly(2));
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.MarkCompleted(), Times.Once());
        }

        [Test]
        public void sleeps_the_shorter_delay_when_both_kinds_wait()
        {
            _passes.Enqueue(Pass(3, 1, 1, resolving: 2));
            _passes.Enqueue(Pass(1, 1, 1, resolving: 0));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            // Attempt 1 backs off a minute, so the 30 second resolve delay wins the first pass
            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(LegacyRetryPolicy.PendingDelay), Times.Once());
            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.FromMinutes(1)), Times.Once());
        }

        [Test]
        public void never_completes_while_items_pending()
        {
            _passes.Enqueue(Pass(3));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.MarkCompleted(), Times.Never());
            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(It.IsAny<TimeSpan>()), Times.Never());
        }

        [Test]
        public void no_runner_logs_and_does_not_complete()
        {
            _runner.Setup(r => r.Handles(It.IsAny<LegacyMigrationState>())).Returns(false);

            Subject.Execute(new LegacyMigrationImportCommand());

            ExceptionVerification.ExpectedErrors(1);
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.MarkCompleted(), Times.Never());
        }

        [Test]
        public void several_runners_report_needs_attention_and_do_not_complete()
        {
            var second = new Mock<ILegacyImportRunner>();
            second.Setup(r => r.Handles(It.IsAny<LegacyMigrationState>())).Returns(true);
            Mocker.SetConstant<IEnumerable<ILegacyImportRunner>>(new[] { _runner.Object, second.Object });

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ILegacyMigrationService>()
                  .Verify(s => s.Report(LegacyReportCategory.NeedsAttention, "Migration", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Once());
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.MarkCompleted(), Times.Never());
            _runner.Verify(r => r.RunPass(It.IsAny<LegacyMigrationState>(), It.IsAny<ILegacyImportProgress>()), Times.Never());
        }

        [Test]
        public void progress_reports_real_percentage()
        {
            _duringPass = p =>
            {
                p.Total(4);
                p.Advance("Series");
            };
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<IReportCommandProgress>().Verify(r => r.Report("Series", 1, 4, 25), Times.Once());
        }
    }
}
