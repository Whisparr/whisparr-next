using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.Fingerprints.Commands
{
    public class GenerateOsHashesCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
