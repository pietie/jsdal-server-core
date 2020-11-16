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

namespace Extensions
{
    // internal class Middleware : MiddlewareBase
    // {
    //     private readonly RequestDelegate _next;

    //     public Middleware(RequestDelegate next, MirrorSharp.MirrorSharpOptions options) : base(options)
    //     {
    //         //_next = Argument.NotNull(nameof(next), next);
    //         _next = next;
    //     }

    //     public Task InvokeAsync(HttpContext context)
    //     {
    //         if (!context.WebSockets.IsWebSocketRequest || !context.Request.Path.HasValue || !context.Request.Path.Value.Equals("/mirrorsharp", StringComparison.OrdinalIgnoreCase))
    //             return _next(context);

    //         return StartWebSocketLoopAsync(context);
    //     }

    //     public async Task StartWebSocketLoopAsync(HttpContext context)
    //     {
    //         var webSocket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
    //         await WebSocketLoopAsync(webSocket, CancellationToken.None);
    //     }
    // }

}