using System;
using System.Collections.Generic;
using FluentAssertions;
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
        private List<bool> _recordOutages;
        private DateTime _now;

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
            _recordOutages = new List<bool>();
            _now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

            Mocker.GetMock<ISkyhookPacer>().SetupGet(p => p.UtcNow).Returns(() => _now);

            _runner.Setup(r => r.RunPass(It.IsAny<LegacyMigrationState>(), It.IsAny<ILegacyImportProgress>(), It.IsAny<bool>()))
                   .Returns((LegacyMigrationState s, ILegacyImportProgress p, bool recordOutages) =>
                   {
                       _recordOutages.Add(recordOutages);
                       _duringPass?.Invoke(p);

                       return _passes.Dequeue();
                   });

            Mocker.SetConstant<IEnumerable<ILegacyImportRunner>>(new[] { _runner.Object });
        }

        private LegacyImportPass Pass(int pending, int waiting = 0, TimeSpan? dueIn = null, int resolving = 0, int attempted = 1, bool outage = false)
        {
            return new LegacyImportPass
            {
                Pending = pending,
                WaitingRetry = waiting,
                NextDue = dueIn.HasValue ? _now + dueIn.Value : null,
                Waiting = resolving,
                Attempted = attempted,
                Outage = outage
            };
        }

        private void VerifyRunPasses(Times times)
        {
            _runner.Verify(r => r.RunPass(It.IsAny<LegacyMigrationState>(), It.IsAny<ILegacyImportProgress>(), It.IsAny<bool>()), times);
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

            VerifyRunPasses(Times.Never());
        }

        [Test]
        public void completes_when_runner_reports_nothing_pending()
        {
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.MarkCompleted(), Times.Once());
        }

        [Test]
        public void sleeps_until_the_earliest_item_is_due_then_runs_again()
        {
            _passes.Enqueue(Pass(2, 2, TimeSpan.FromMinutes(5)));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.FromMinutes(5)), Times.Once());
            VerifyRunPasses(Times.Exactly(2));
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.MarkCompleted(), Times.Once());
        }

        [Test]
        public void sleeps_a_long_backoff_in_full_when_nothing_else_waits()
        {
            _passes.Enqueue(Pass(1, 1, TimeSpan.FromHours(6)));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.FromHours(6)), Times.Once());
        }

        [Test]
        public void does_not_sleep_a_negative_time_when_an_item_is_already_due()
        {
            _passes.Enqueue(Pass(1, 1, TimeSpan.FromMinutes(-3)));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.Zero), Times.Once());
        }

        [Test]
        public void a_pass_where_every_item_failed_backs_off_globally_on_the_policy_schedule()
        {
            _passes.Enqueue(Pass(3, 3, outage: true));
            _passes.Enqueue(Pass(3, 3, outage: true));
            _passes.Enqueue(Pass(3, 3, outage: true));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            var pacer = Mocker.GetMock<ISkyhookPacer>();

            pacer.Verify(p => p.Sleep(TimeSpan.FromMinutes(5)), Times.Once());
            pacer.Verify(p => p.Sleep(TimeSpan.FromMinutes(30)), Times.Once());
            pacer.Verify(p => p.Sleep(TimeSpan.FromHours(2)), Times.Once());
            _recordOutages.Should().Equal(false, false, false, false);
        }

        [Test]
        public void outage_passes_start_spending_attempts_once_the_global_backoff_is_used_up()
        {
            for (var i = 0; i < LegacyRetryPolicy.MaxAttempts; i++)
            {
                _passes.Enqueue(Pass(1, 1, outage: true));
            }

            // The pass told to record outages sets the item's own backoff
            _passes.Enqueue(Pass(1, 1, TimeSpan.FromMinutes(5), outage: true));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            _recordOutages.Should().Equal(false, false, false, false, false, true, true);
            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.FromHours(6)), Times.Exactly(2));
            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.FromMinutes(5)), Times.Exactly(2));
        }

        [Test]
        public void a_pass_that_worked_on_something_ends_the_outage()
        {
            _passes.Enqueue(Pass(2, 2, outage: true));
            _passes.Enqueue(Pass(1, 1, TimeSpan.FromMinutes(5)));
            _passes.Enqueue(Pass(2, 2, outage: true));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            // The second outage starts the schedule over rather than continuing it
            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.FromMinutes(5)), Times.Exactly(3));
        }

        [Test]
        public void a_pass_that_only_skipped_backing_off_items_keeps_the_outage_count()
        {
            _passes.Enqueue(Pass(2, 2, outage: true));
            _passes.Enqueue(Pass(2, 2, TimeSpan.FromMinutes(5), attempted: 0));
            _passes.Enqueue(Pass(2, 2, outage: true));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.FromMinutes(30)), Times.Once());
        }

        [Test]
        public void sleeps_the_pending_delay_while_skyhook_is_resolving()
        {
            _passes.Enqueue(Pass(2, resolving: 2));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(LegacyRetryPolicy.PendingDelay), Times.Once());
            VerifyRunPasses(Times.Exactly(2));
            Mocker.GetMock<ILegacyMigrationService>().Verify(s => s.MarkCompleted(), Times.Once());
        }

        [Test]
        public void sleeps_the_shorter_delay_when_both_kinds_wait()
        {
            _passes.Enqueue(Pass(3, 1, TimeSpan.FromMinutes(5), resolving: 2));
            _passes.Enqueue(Pass(2, 1, TimeSpan.FromSeconds(10), resolving: 1));
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            // The 5 minute backoff is capped at the 30 second resolve delay; a 10 second one is shorter, so it wins
            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(LegacyRetryPolicy.PendingDelay), Times.Once());
            Mocker.GetMock<ISkyhookPacer>().Verify(p => p.Sleep(TimeSpan.FromSeconds(10)), Times.Once());
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
            VerifyRunPasses(Times.Never());
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

            Mocker.GetMock<IReportCommandProgress>().Verify(r => r.Report("Series", 1, 4, 25, null), Times.Once());
        }

        [Test]
        public void progress_reports_what_is_left_of_each_phase()
        {
            _duringPass = p =>
            {
                p.Total(5);
                p.PhaseTotal("Studios", 2);
                p.PhaseTotal("Performers", 3);
                p.Advance("Studios");
                p.Advance("Studios");
                p.Advance("Performers", 2);
            };
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            var reporter = Mocker.GetMock<IReportCommandProgress>();

            reporter.Verify(r => r.Report("Studios", 1, 5, 20, 1L), Times.Once());
            reporter.Verify(r => r.Report("Studios", 2, 5, 40, 0L), Times.Once());
            reporter.Verify(r => r.Report("Performers", 4, 5, 80, 1L), Times.Once());
        }

        [Test]
        public void status_shows_the_step_without_moving_the_overall_count()
        {
            _duringPass = p =>
            {
                p.Total(4);
                p.Advance("Performers");
                p.Status("Hashing files", 100);
            };
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<IReportCommandProgress>().Verify(r => r.Report("Hashing files", 1, 4, 25, 100L), Times.Once());
        }

        [Test]
        public void a_new_pass_forgets_the_last_passes_phase_totals()
        {
            _duringPass = p =>
            {
                p.Total(2);
                p.PhaseTotal("Studios", 2);
                p.Total(2);
                p.Advance("Studios");
            };
            _passes.Enqueue(Pass(0));

            Subject.Execute(new LegacyMigrationImportCommand());

            Mocker.GetMock<IReportCommandProgress>().Verify(r => r.Report("Studios", 1, 2, 50, null), Times.Once());
        }
    }
}
