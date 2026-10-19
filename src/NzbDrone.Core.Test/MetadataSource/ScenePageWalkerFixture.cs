using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class ScenePageWalkerFixture : CoreTest<ScenePageWalker>
    {
        private FakePacer _pacer;
        private List<string> _requested;
        private List<ScenePage> _handled;
        private Func<bool> _keepGoing;

        private class FakePacer : ISkyhookPacer
        {
            public DateTime UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public List<TimeSpan> Sleeps { get; } = new();

            public void Sleep(TimeSpan duration)
            {
                Sleeps.Add(duration);
                UtcNow += duration;
            }
        }

        [SetUp]
        public void Setup()
        {
            _pacer = new FakePacer();
            _requested = new List<string>();
            _handled = new List<ScenePage>();
            _keepGoing = () => true;
            Mocker.SetConstant<ISkyhookPacer>(_pacer);
        }

        private static ScenePage Page(ScenePageStatus status, string next = null, string tag = null, params string[] pending)
        {
            return new ScenePage
            {
                Status = status,
                NextCursor = next,
                Pending = pending.ToList(),
                RetryAfter = TimeSpan.FromSeconds(10),
                Items = new List<ScenePageItem> { new() { SceneId = tag ?? "item" } }
            };
        }

        private ScenePageWalkResult Walk(Func<string, int, ScenePage> respond)
        {
            var calls = new Dictionary<string, int>();

            return Subject.Walk(
                cursor =>
                {
                    _requested.Add(cursor);
                    var key = cursor ?? "<null>";
                    calls[key] = calls.GetValueOrDefault(key) + 1;
                    return respond(cursor, calls[key]);
                },
                page => _handled.Add(page),
                () => _keepGoing());
        }

        [Test]
        public void should_walk_every_page_until_next_cursor_is_null()
        {
            var result = Walk((cursor, _) => cursor switch
            {
                null => Page(ScenePageStatus.Complete, "a", "p1"),
                "a" => Page(ScenePageStatus.Complete, "b", "p2"),
                _ => Page(ScenePageStatus.Complete, null, "p3")
            });

            _handled.Select(p => p.Items[0].SceneId).Should().Equal("p1", "p2", "p3");
            result.Pages.Should().Be(3);
            result.GaveUp.Should().BeEmpty();
            _pacer.Sleeps.Should().BeEmpty();
        }

        [Test]
        public void should_move_on_from_a_pending_page_and_revisit_it()
        {
            Walk((cursor, call) => cursor switch
            {
                null when call == 1 => Page(ScenePageStatus.Pending, "a", "p1-202", "x"),
                null => Page(ScenePageStatus.Complete, "a", "p1-200"),
                _ => Page(ScenePageStatus.Complete, null, "p2")
            });

            _handled.Select(p => p.Items[0].SceneId).Should().Equal("p1-202", "p2", "p1-200");
        }

        [Test]
        public void should_wait_retry_after_and_retry_when_throttled()
        {
            var throttled = Page(ScenePageStatus.Throttled);
            throttled.RetryAfter = TimeSpan.FromSeconds(30);
            throttled.Items.Clear();

            var result = Walk((_, call) => call == 1 ? throttled : Page(ScenePageStatus.Complete));

            _pacer.Sleeps.Should().Equal(TimeSpan.FromSeconds(30));
            _requested.Should().Equal(null, null);
            _handled.Should().HaveCount(1);
            result.Pages.Should().Be(1);
        }

        [Test]
        public void should_give_up_on_a_page_after_five_minutes_without_progress()
        {
            var result = Walk((_, _) => Page(ScenePageStatus.Pending, null, "p1", "x", "y"));

            result.GaveUp.Should().Equal("x", "y");

            // first pass + 30 revisits, each preceded by a 10s sleep, then no further fetches
            _requested.Should().HaveCount(31);
            _pacer.UtcNow.Should().Be(new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_keep_pending_ids_when_a_revisit_gives_up_right_after_a_throttle()
        {
            var result = Walk((_, call) =>
            {
                if (call % 2 == 1 && call > 1)
                {
                    var throttled = Page(ScenePageStatus.Throttled);
                    throttled.Items.Clear();
                    return throttled;
                }

                return Page(ScenePageStatus.Pending, null, "p1", "x", "y");
            });

            result.GaveUp.Should().Equal("x", "y");
            _requested.Should().HaveCount(31);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_stop_walking_when_the_first_pass_is_throttled_for_five_minutes()
        {
            var result = Walk((cursor, _) =>
            {
                if (cursor == null)
                {
                    return Page(ScenePageStatus.Complete, "a", "p1");
                }

                var throttled = Page(ScenePageStatus.Throttled, "b");
                throttled.RetryAfter = TimeSpan.FromMinutes(1);
                throttled.Items.Clear();
                return throttled;
            });

            result.GaveUp.Should().Equal("cursor:a");
            _handled.Should().HaveCount(1);
            result.Pages.Should().Be(1);
            _requested.Should().NotContain("b");
            _requested.Count(c => c == "a").Should().Be(6);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_give_up_on_earlier_pending_pages_when_the_first_pass_is_throttled_out()
        {
            var result = Walk((cursor, _) =>
            {
                if (cursor == null)
                {
                    return Page(ScenePageStatus.Pending, "a", "p1", "x", "y");
                }

                var throttled = Page(ScenePageStatus.Throttled, "b");
                throttled.RetryAfter = TimeSpan.FromMinutes(1);
                throttled.Items.Clear();
                return throttled;
            });

            result.GaveUp.Should().Equal("cursor:a", "x", "y");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_stop_the_first_pass_when_a_cursor_repeats_and_still_revisit()
        {
            var result = Walk((cursor, call) => cursor switch
            {
                null when call == 1 => Page(ScenePageStatus.Pending, "a", "p1-202", "x"),
                null => Page(ScenePageStatus.Complete, "a", "p1-200"),
                _ => Page(ScenePageStatus.Complete, "a", "p2")
            });

            _handled.Select(p => p.Items[0].SceneId).Should().Equal("p1-202", "p2", "p1-200");
            result.GaveUp.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_stop_before_fetching_the_next_page_when_told_to()
        {
            var fetches = 0;
            _keepGoing = () => fetches < 2;

            var result = Walk((cursor, _) =>
            {
                fetches++;
                return Page(ScenePageStatus.Complete, cursor == null ? "a" : "b", "p");
            });

            result.Stopped.Should().BeTrue();
            result.Pages.Should().Be(2);
            _requested.Should().Equal(null, "a");
        }

        [Test]
        public void should_stop_revisiting_when_told_to()
        {
            var keep = true;
            _keepGoing = () => keep;

            var result = Walk((cursor, call) =>
            {
                keep = false;
                return Page(ScenePageStatus.Pending, null, "p1", "x");
            });

            result.Stopped.Should().BeTrue();
            _requested.Should().HaveCount(1);
        }

        [Test]
        public void should_reset_the_stall_clock_when_pending_shrinks()
        {
            var sizes = new[] { 3, 3, 2, 2, 1, 1, 0 };

            var result = Walk((_, call) =>
            {
                var size = sizes[Math.Min(call - 1, sizes.Length - 1)];
                var page = size == 0
                    ? Page(ScenePageStatus.Complete)
                    : Page(ScenePageStatus.Pending, null, "p", Enumerable.Range(0, size).Select(i => $"id{i}").ToArray());
                page.RetryAfter = TimeSpan.FromMinutes(2);
                return page;
            });

            result.GaveUp.Should().BeEmpty();
            _handled.Should().HaveCount(sizes.Length);
            _pacer.UtcNow.Should().BeAfter(new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc));
        }

        [Test]
        public void should_propagate_fetch_exceptions()
        {
            var exception = new HttpException(new HttpResponse(new HttpRequest("http://localhost/x"), new HttpHeader(), ""));

            Action act = () => Walk((cursor, _) => cursor == null
                ? Page(ScenePageStatus.Complete, "a", "p1")
                : throw exception);

            act.Should().Throw<HttpException>().Which.Should().BeSameAs(exception);
            _handled.Should().HaveCount(1);
        }
    }
}
