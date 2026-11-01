using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.RootFolders.Commands
{
    // Only ever pushed by the user from the Unmapped Files page; it is never scheduled.
    public class FindUnmappedFileMatchesCommand : Command
    {
        private string _completionMessage;

        public List<int> FileIds { get; set; } = new();

        public override bool SendUpdatesToClient => true;
        public override string CompletionMessage => _completionMessage;

        public void SetCompletionMessage(string message)
        {
            _completionMessage = message;
        }
    }
}
