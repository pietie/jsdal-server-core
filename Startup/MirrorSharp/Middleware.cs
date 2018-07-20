using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MirrorSharp.Internal;

namespace MirrorSharp.Owin.Internal
{
    using AppFunc = Func<IDictionary<string, object>, Task>;
    using WebSocketAccept = Action<IDictionary<string, object>, Func<IDictionary<string, object>, Task>>;

    internal class MirrorSharpMiddleware : MiddlewareBase
    {
        private readonly RequestDelegate _next;

        //public MirrorSharpMiddleware(AppFunc next, MirrorSharpOptions options) : base(options)
        //{
        //_next = next;//Argument.NotNull(nameof(next), next);
        //}
        public MirrorSharpMiddleware(RequestDelegate next, IHostingEnvironment hostingEnv, IOptions<MirrorSharpOptions> options, ILoggerFactory loggerFactory) : base(options.Value)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            if (hostingEnv == null)
            {
                throw new ArgumentNullException(nameof(hostingEnv));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _next = next;
            //!_options = options.Value;
            //!_contentTypeProvider = options.Value.ContentTypeProvider ?? new FileExtensionContentTypeProvider();
            //!_fileProvider = _options.FileProvider ?? Helpers.ResolveFileProvider(hostingEnv);
            //!_matchUrl = _options.RequestPath;
            //!_logger = loggerFactory.CreateLogger<StaticFileMiddleware>();
        }


        //!public Task Invoke(IDictionary<string, object> environment) {
        public async Task Invoke(HttpContext context)
        {
            try
            {
            //if (!context.WebSockets.IsWebSocketRequest)
            if (!context.Request.Path.Equals("/mirrorsharp", StringComparison.OrdinalIgnoreCase))
            {
                if (_next != null) await _next(context);
                return;//? _next(context);
            }


            var socket = await context.WebSockets.AcceptWebSocketAsync();


            //var wsCtx = (WebSocketContext)contextAsObject;
            //var callCancelled = (CancellationToken)e["websocket.CallCancelled"];
            CancellationToken callCancelled = CancellationToken.None;

            // there is a weird issue where a socket never gets closed (deadlock?)
            // if the loop is done in the standard ASP.NET thread
            await Task.Run(
                () => WebSocketLoopAsync(socket, callCancelled), callCancelled
            );

            // await _webSocketHandler.OnConnected(socket);

            // await Receive(socket, async (result, buffer) =>
            // {
            //     if (result.MessageType == WebSocketMessageType.Text)
            //     {
            //         await _webSocketHandler.ReceiveAsync(socket, result, buffer);
            //         return;
            //     }

            //     else if (result.MessageType == WebSocketMessageType.Close)
            //     {
            //         await _webSocketHandler.OnDisconnected(socket);
            //         return;
            //     }

            // });

            /****

            object accept;

            if (!environment.TryGetValue("websocket.Accept", out accept))
                return _next(environment);

            ((WebSocketAccept)accept)(null, async e =>
            {
                var contextKey = typeof(WebSocketContext).FullName;
                if (!e.TryGetValue(contextKey, out var contextAsObject) || contextAsObject == null)
                {
                    throw new NotSupportedException(
                         $"At the moment, MirrorSharp requires Owin host to provide '{contextKey}'.\r\n" +
                          "It's not in the specification, but it is provided by the IIS host at least. " +
                          "After spending some time on this, I don't feel that a WebSocket wrapper for Owin " +
                          "is worth the effort. However if you want to implement one, I will appreciate it.\r\n" +
                          "You can find my attempt at https://gist.github.com/ashmind/40563ead5b467a243308a02d27c707ed."
                    );
                }

                var wsCtx = (WebSocketContext)contextAsObject;
                var callCancelled = (CancellationToken)e["websocket.CallCancelled"];
                // there is a weird issue where a socket never gets closed (deadlock?)
                // if the loop is done in the standard ASP.NET thread
                await Task.Run(
                    () => WebSocketLoopAsync(wsCtx.WebSocket, callCancelled),
                    callCancelled

                );
                 
            });*/

            return;// _next(context);
            //return Task.CompletedTask;
            }
            catch(Exception ex)
            {
                // TODO: Do something real interesting with the exception
                return;
            }
        }
    }
}