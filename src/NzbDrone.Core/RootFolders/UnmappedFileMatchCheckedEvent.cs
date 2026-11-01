using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.RootFolders
{
    // Published once the stored match of an unmapped file has changed
    public class UnmappedFileMatchCheckedEvent : IEvent
    {
        public ImportFile File { get; }

        public UnmappedFileMatchCheckedEvent(ImportFile file)
        {
            File = file;
        }
    }
}
