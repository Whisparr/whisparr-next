namespace NzbDrone.Core.Datastore.Migration.Setup
{
    public interface ISetupModeState
    {
        // True while a legacy install is waiting to be migrated, so v5 runs without its main database.
        bool IsActive { get; }

        DetectionResult Detection { get; }

        // True when setup entry created config5.xml, so a discard may remove it again.
        bool SeededConfig5 { get; }
    }

    // A registered instance. Bootstrap builds it before the container exists.
    public class SetupModeState : ISetupModeState
    {
        public static readonly ISetupModeState Inactive = new SetupModeState(new DetectionResult(), false);

        public SetupModeState(DetectionResult detection, bool seededConfig5)
        {
            Detection = detection ?? new DetectionResult();
            SeededConfig5 = seededConfig5;
        }

        public bool IsActive => Detection.SetupModeRequired;

        public DetectionResult Detection { get; }

        public bool SeededConfig5 { get; }
    }
}
