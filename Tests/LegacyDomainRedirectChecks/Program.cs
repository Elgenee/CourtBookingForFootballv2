using CourtBookingSystem.Middleware;
using Microsoft.AspNetCore.Http;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
    Console.WriteLine($"PASS {message}");
}

async Task<(HttpContext Context, bool NextCalled)> Request(
    string host, string scheme = "http", string path = "/", string query = "", string method = "GET", string pathBase = "")
{
    var nextCalled = false;
    var middleware = new LegacyDomainRedirectMiddleware(_ =>
    {
        nextCalled = true;
        return Task.CompletedTask;
    });
    var context = new DefaultHttpContext();
    context.Request.Host = new HostString(host);
    context.Request.Scheme = scheme;
    context.Request.PathBase = pathBase;
    context.Request.Path = path;
    context.Request.QueryString = new QueryString(query);
    context.Request.Method = method;
    await middleware.InvokeAsync(context);
    return (context, nextCalled);
}

foreach (var host in new[] { "GiuseppeFootball.somee.com", "www.GiuseppeFootball.somee.com", "GIUSEPPEFOOTBALL.SOMEE.COM", "www.giuseppefootball.somee.com:80" })
{
    var (context, next) = await Request(host);
    Check(context.Response.StatusCode == 308 && context.Response.Headers.Location == "https://www.campocebu.com/" && !next,
        $"{host} redirects directly to the HTTPS custom domain");
}

var (booking, _) = await Request("GiuseppeFootball.somee.com", path: "/Bookings/Create", query: "?courtId=1&date=2026-10-21");
Check(booking.Response.Headers.Location == "https://www.campocebu.com/Bookings/Create?courtId=1&date=2026-10-21", "Booking paths and query parameters are retained");
var (encoded, _) = await Request("www.GiuseppeFootball.somee.com", path: "/images/field photo.jpg", query: "?returnUrl=%2FBookings%2FCreate&name=A%26B", pathBase: "/app");
Check(encoded.Response.Headers.Location == "https://www.campocebu.com/app/images/field%20photo.jpg?returnUrl=%2FBookings%2FCreate&name=A%26B", "Path bases, spaces, and encoded query values are retained correctly");
var (secure, _) = await Request("GiuseppeFootball.somee.com", scheme: "https");
Check(secure.Response.StatusCode == 308, "HTTPS requests on an old domain also redirect");
foreach (var method in new[] { "POST", "HEAD" })
{
    var (context, next) = await Request("GiuseppeFootball.somee.com", path: "/Bookings/Create", method: method);
    Check(context.Response.StatusCode == 308 && !next, $"{method} uses a permanent redirect that preserves the request method");
}
foreach (var host in new[] { "www.campocebu.com", "campocebu.com", "localhost:5000", "other.somee.com", "GiuseppeFootball.somee.com.example.com" })
{
    var (context, next) = await Request(host);
    Check(next && !context.Response.Headers.ContainsKey("Location"), $"{host} continues normally without a redirect loop");
}
Console.WriteLine($"{checks} legacy domain redirect checks passed.");
