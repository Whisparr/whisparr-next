using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    // What the user picked on the setup page: which install to migrate and which metadata sources to lock.
    public class SetupChoice
    {
        public LegacyKind Kind { get; set; }
        public SceneMetadataType SceneSource { get; set; }
        public MovieMetadataType MovieSource { get; set; }
    }
}
