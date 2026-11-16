using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.RootFolders.Commands
{
    // Only ever pushed by the user from the Unmapped Files page; it is never scheduled.
    public class FindUnmappedFileMatchesCommand : Command
    {
        private string _completionMessage;

        public List<int> FileIds { get; set; } = new();

        // Set by the page it was started from: Scenes, Movies or JAV
        public ItemType ItemType { get; set; } = ItemType.Scene;

        public override bool SendUpdatesToClient => true;
        public override string CompletionMessage => _completionMessage;

        public void SetCompletionMessage(string message)
        {
            _completionMessage = message;
        }
    }
}
