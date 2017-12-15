using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Sample
{
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class Chat : Hub
    {
        public Task Send(string message)
        {
            return Clients.All.InvokeAsync("Send", message);
        }
    }
}