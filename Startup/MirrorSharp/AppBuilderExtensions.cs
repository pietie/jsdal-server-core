using System;
using Microsoft.AspNetCore.Builder;
using MirrorSharp.Advanced;

namespace Extensions {

    // public static class ApplicationBuilderExtensions {
    //     /// <summary>Adds MirrorSharp middleware to the <see cref="IApplicationBuilder" />.</summary>
    //     /// <param name="app">The app builder.</param>
    //     /// <param name="options">The <see cref="MirrorSharpOptions" /> object used by the MirrorSharp middleware.</param>
    //     public static IApplicationBuilder UseMirrorSharp(this IApplicationBuilder app, MirrorSharp.MirrorSharpOptions options = null) {
    //         //Argument.NotNull(nameof(app), app);
    //         app.UseMiddleware<Middleware>(options ?? new MirrorSharp.MirrorSharpOptions());
    //         return app;
    //     }
    // }

    // public class MirrorSharpExceptionLogger : MirrorSharp.Advanced.IExceptionLogger
    // {
    //     public void LogException(Exception exception, IWorkSession session)
    //     {
            
    //     }
    // }
}
