using Microsoft.AspNetCore.Http.Extensions;

namespace CourtBookingSystem.Middleware
{
    public sealed class LegacyDomainRedirectMiddleware
    {
        private readonly RequestDelegate _next;

        public LegacyDomainRedirectMiddleware(RequestDelegate next) => _next = next;

        public Task InvokeAsync(HttpContext context)
        {
            var host = context.Request.Host.Host;
            if (string.Equals(host, "GiuseppeFootball.somee.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "www.GiuseppeFootball.somee.com", StringComparison.OrdinalIgnoreCase))
            {
                var destination = UriHelper.BuildAbsolute(
                    "https",
                    new HostString("www.campocebu.com"),
                    context.Request.PathBase,
                    context.Request.Path,
                    context.Request.QueryString);

                // A permanent redirect that also preserves submitted request methods and bodies.
                context.Response.Redirect(destination, permanent: true, preserveMethod: true);
                return Task.CompletedTask;
            }

            return _next(context);
        }
    }
}
