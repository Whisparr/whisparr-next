using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.RootFolders.Commands
{
    public class ScanUnmappedFilesCommand : Command
    {
        public override bool SendUpdatesToClient => true;
    }
}
