using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Composition;
using NzbDrone.Common.Serializer;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ProgressMessaging;
using NzbDrone.SignalR;
using Whisparr.Http;
using Whisparr.Http.REST;
using Whisparr.Http.REST.Attributes;
using Whisparr.Http.Validation;

namespace Whisparr.Api.V5.Commands
{
    [V5ApiController]
    public class CommandController : RestControllerWithSignalR<CommandResource, CommandModel>, IHandle<CommandUpdatedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly KnownTypes _knownTypes;
        private readonly Debouncer _debouncer;
        private readonly Dictionary<int, CommandResource> _pendingUpdates;
        private readonly IBroadcastResourceIds _idBroadcaster;

        private readonly CommandPriorityComparer _commandPriorityComparer = new CommandPriorityComparer();

        public CommandController(IManageCommandQueue commandQueueManager,
                             IBroadcastSignalRMessage signalRBroadcaster,
                             KnownTypes knownTypes,
                             IBroadcastResourceIds idBroadcaster)
            : base(signalRBroadcaster)
        {
            _commandQueueManager = commandQueueManager;
            _knownTypes = knownTypes;
            _idBroadcaster = idBroadcaster;

            _debouncer = new Debouncer(SendUpdates, TimeSpan.FromSeconds(0.1));
            _pendingUpdates = new Dictionary<int, CommandResource>();

            PostValidator.RuleFor(c => c.Name).NotBlank();
        }

        protected override CommandResource GetResourceById(int id)
        {
            return _commandQueueManager.Get(id).ToResource();
        }

        [RestPostById]
        [Consumes("application/json")]
        [Produces("application/json")]
        public ActionResult<CommandResource> StartCommand([FromBody] CommandResource commandResource)
        {
            var commandType =
                _knownTypes.GetImplementations(typeof(Command))
                    .SingleOrDefault(c => c.Name.Replace("Command", "")
                        .Equals(commandResource.Name, StringComparison.InvariantCultureIgnoreCase));

            if (commandType == null)
            {
                return BadRequest($"Unknown command type: {commandResource.Name}");
            }

            Request.Body.Seek(0, SeekOrigin.Begin);
            using (var reader = new StreamReader(Request.Body))
            {
                var body = reader.ReadToEnd();
                var command = STJson.Deserialize(body, commandType) as Command;

                command.SuppressMessages = !command.SendUpdatesToClient;
                command.SendUpdatesToClient = true;
                command.ClientUserAgent = Request.Headers["UserAgent"];

                var trackedCommand = _commandQueueManager.Push(command, commandResource.Priority, CommandTrigger.Manual);

                return Created(trackedCommand.Id);
            }
        }

        [HttpGet]
        public List<CommandResource> GetStartedCommands()
        {
            return _commandQueueManager.All()
                .OrderBy(c => c.Status, _commandPriorityComparer)
                .ThenByDescending(c => c.Priority)
                .ToResource();
        }

        [RestDeleteById]
        public void CancelCommand(int id)
        {
            _commandQueueManager.Cancel(id);
        }

        [NonAction]
        public void Handle(CommandUpdatedEvent message)
        {
            if (message.Command.Body.SendUpdatesToClient)
            {
                lock (_pendingUpdates)
                {
                    _pendingUpdates[message.Command.Id] = message.Command.ToResource();
                }

                _debouncer.Execute();
            }
        }

        private void SendUpdates()
        {
            lock (_pendingUpdates)
            {
                var pendingUpdates = _pendingUpdates.Values.ToArray();
                _pendingUpdates.Clear();

                // The UI flushes its held-back refreshes on a finished command, so the command's last batched ids must reach it first
                if (pendingUpdates.Any(u => u.Status is CommandStatus.Completed or CommandStatus.Failed))
                {
                    _idBroadcaster.Flush();
                }

                foreach (var pendingUpdate in pendingUpdates)
                {
                    BroadcastResourceChange(ModelAction.Updated, pendingUpdate);

                    if (pendingUpdate.Name == typeof(MessagingCleanupCommand).Name.Replace("Command", "") &&
                        pendingUpdate.Status == CommandStatus.Completed)
                    {
                        BroadcastResourceChange(ModelAction.Sync);
                    }
                }
            }
        }
    }
}
