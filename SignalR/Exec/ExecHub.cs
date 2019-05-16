using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using jsdal_server_core.Controllers;
using jsdal_server_core.Performance;
using Microsoft.AspNetCore.SignalR;

namespace jsdal_server_core.Hubs
{
    public class ExecHub : Hub
    {
        public ExecHub()
        {

        }

        public override Task OnConnectedAsync()
        {
            // var key = Context.ConnectionId; // TODO: Change to something like the logged in userId

            // _connections.Add(key, Context.ConnectionId);

            // Groups.AddToGroupAsync(Context.ConnectionId, "RealtimeHub.Main");

            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            // var key = Context.ConnectionId; // TODO: Change to something like the logged in userId
            // _connections.Remove(key, Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        public ApiResponse Exec(string endpoint, string schema, string routine, Dictionary<string, string> parameters, ExecController.ExecType type, string appTitle)
        {
            var endpointElems = endpoint.Split('/'); // TODO: error handling

            var execOptions = new ExecController.ExecOptions() { project = endpointElems[0], application = endpointElems[1], endpoint = endpointElems[2], schema = schema, routine = routine, type = type };
            (var result, var routineExecutionMetric, var mayAccess) = ExecController.ExecuteRoutine(execOptions, parameters, null/*requestHeaders*/, "$WEB SOCKETS$", null, appTitle, out var responseHeaders);

            if (mayAccess != null && !mayAccess.isSuccess)
            {
                throw new Exception("Unauthorised access");
            }

            return result;
        }

        public Guid ExecAsync(string test)
        {
            var execGuid = Guid.NewGuid();
            // TODO: Tie execGuid to connection?

            return execGuid;
        }
    }

}