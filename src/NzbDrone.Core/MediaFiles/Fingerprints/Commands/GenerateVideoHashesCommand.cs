using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.Fingerprints.Commands
{
    public class GenerateVideoHashesCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
