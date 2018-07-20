using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using MirrorSharp.Owin.Internal;


namespace MirrorSharp.Owin
{
    /// <summary>MirrorSharp-related extensions for the <see cref="IAppBuilder" />.</summary>
    public static class AppBuilderExtensions
    {
        /// <summary>Adds MirrorSharp middleware to the <see cref="IAppBuilder" />.</summary>
        /// <param name="app">The app builder.</param>
        /// <param name="options">The <see cref="MirrorSharpOptions" /> object used by the MirrorSharp middleware.</param>

        public static IApplicationBuilder UseMirrorSharp(this IApplicationBuilder app, MirrorSharpOptions options = null)
        {
            //!?app.Use(typeof(Middleware), options ?? new MirrorSharpOptions());
            var o = Options.Create(options);
            return app.UseMiddleware<MirrorSharpMiddleware>(o);


        }
    }
}
