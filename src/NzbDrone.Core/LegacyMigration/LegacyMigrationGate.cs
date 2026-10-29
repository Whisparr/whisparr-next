namespace NzbDrone.Core.LegacyMigration
{
    public interface ILegacyMigrationGate
    {
        bool AutomationPaused { get; }
    }

    public class LegacyMigrationGate : ILegacyMigrationGate
    {
        private readonly ILegacyMigrationService _migrationService;

        public LegacyMigrationGate(ILegacyMigrationService migrationService)
        {
            _migrationService = migrationService;
        }

        public bool AutomationPaused => _migrationService.IsIncomplete();
    }
}
