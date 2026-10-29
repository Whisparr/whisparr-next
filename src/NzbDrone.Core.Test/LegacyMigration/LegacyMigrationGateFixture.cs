using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.LegacyMigration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.LegacyMigration
{
    [TestFixture]
    public class LegacyMigrationGateFixture : CoreTest<LegacyMigrationGate>
    {
        [TestCase(true)]
        [TestCase(false)]
        public void should_pause_automation_while_migration_is_incomplete(bool incomplete)
        {
            Mocker.GetMock<ILegacyMigrationService>()
                  .Setup(s => s.IsIncomplete())
                  .Returns(incomplete);

            Subject.AutomationPaused.Should().Be(incomplete);
        }
    }
}
