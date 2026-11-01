using NzbDrone.Common.Disk;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.SignalR;
using Whisparr.Http;

namespace Whisparr.Api.V5.UnmappedFiles
{
    // UnmappedFileController isn't a RestControllerWithSignalR, so the row updates are pushed from here.
    public class UnmappedFileBroadcaster : IHandle<UnmappedFileMatchCheckedEvent>
    {
        private const string ResourceName = "unmappedfile";
        private const int ApiVersion = 5;

        private readonly IBroadcastSignalRMessage _signalRBroadcaster;
        private readonly IDiskProvider _diskProvider;

        public UnmappedFileBroadcaster(IBroadcastSignalRMessage signalRBroadcaster, IDiskProvider diskProvider)
        {
            _signalRBroadcaster = signalRBroadcaster;
            _diskProvider = diskProvider;
        }

        public void Handle(UnmappedFileMatchCheckedEvent message)
        {
            if (!_signalRBroadcaster.IsConnected)
            {
                return;
            }

            var resource = message.File.ToResource(_diskProvider.FileExists(message.File.Path));

            _signalRBroadcaster.BroadcastMessage(new SignalRMessage
            {
                Name = ResourceName,
                Body = new ResourceChangeMessage<UnmappedFileResource>(resource, ModelAction.Updated),
                Version = ApiVersion,
                Action = ModelAction.Updated
            });
        }
    }
}
