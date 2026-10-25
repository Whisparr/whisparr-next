using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Migration.Setup;

namespace Whisparr.Http.Setup
{
    // The body of POST /api/setup/migrate. Enums travel as camelCase strings, like the rest of the API.
    public class SetupChoiceResource
    {
        public LegacyKind Kind { get; set; }
        public SceneMetadataType SceneSource { get; set; }
        public MovieMetadataType MovieSource { get; set; }

        public SetupChoice ToModel()
        {
            return new SetupChoice
            {
                Kind = Kind,
                SceneSource = SceneSource,
                MovieSource = MovieSource
            };
        }
    }
}
