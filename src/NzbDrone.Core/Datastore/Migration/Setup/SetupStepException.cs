using System;

namespace NzbDrone.Core.Datastore.Migration.Setup
{
    // A setup step failed in a way the user can act on. UserMessage is safe to show on the setup page.
    public class SetupStepException : Exception
    {
        public string UserMessage { get; }

        public SetupStepException(string userMessage, Exception inner = null)
            : base(userMessage, inner)
        {
            UserMessage = userMessage;
        }
    }
}
