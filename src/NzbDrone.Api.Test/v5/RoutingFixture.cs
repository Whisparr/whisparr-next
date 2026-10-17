using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Whisparr.Http;

namespace NzbDrone.Api.Test.v5
{
    [TestFixture]
    public class RoutingFixture
    {
        // The iCal feed stays at feed/v3, so V5 has no copy of it
        private static readonly string[] V3OnlyControllers = { "CalendarFeedController" };

        private static readonly Type[] V5Controllers = ControllersIn(typeof(Whisparr.Api.V5.System.SystemController));
        private static readonly Type[] V3Controllers = ControllersIn(typeof(Whisparr.Api.V3.System.SystemController));

        private static Type[] ControllersIn(Type anchor)
        {
            return anchor.Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
                .ToArray();
        }

        [Test]
        public void every_v5_controller_routes_under_api_v5()
        {
            V5Controllers.Should().NotBeEmpty();

            var misrouted = new List<string>();

            foreach (var type in V5Controllers)
            {
                var hasV5 = type.GetCustomAttributes(true).OfType<V5ApiControllerAttribute>().Any();
                var hasV3 = type.GetCustomAttributes(true).Any(a => a is V3ApiControllerAttribute || a is V3FeedControllerAttribute);

                if (!hasV5 || hasV3)
                {
                    misrouted.Add(type.FullName);
                }
            }

            misrouted.Should().BeEmpty();
        }

        [Test]
        public void every_v3_controller_has_a_v5_twin()
        {
            var v3Names = V3Controllers.Select(t => t.Name).Where(n => !V3OnlyControllers.Contains(n));
            var v5Names = V5Controllers.Select(t => t.Name);

            v5Names.Should().BeEquivalentTo(v3Names);
        }
    }
}
