using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A minimal HTTP server on 127.0.0.1 for resource-policy tests: serves fixed bodies, can
/// answer with a redirect, and counts the requests it receives.
/// </summary>
internal sealed class LoopbackHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, (int Status, string Body, string? Location)> _routes = new(StringComparer.Ordinal);
    private int _requests;

    public int Port { get; }
    public int Requests => Volatile.Read(ref _requests);
    public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

    public LoopbackHttpServer()
    {
        Port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public LoopbackHttpServer Serve(string path, string body)
    {
        lock (_routes) _routes[path] = (200, body, null);
        return this;
    }

    public LoopbackHttpServer Redirect(string path, string location)
    {
        lock (_routes) _routes[path] = (302, "", location);
        return this;
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }
            Interlocked.Increment(ref _requests);
            (int Status, string Body, string? Location) route;
            lock (_routes)
                route = _routes.TryGetValue(ctx.Request.Url!.AbsolutePath, out var r) ? r : (404, "", null);
            ctx.Response.StatusCode = route.Status;
            if (route.Location != null)
                ctx.Response.RedirectLocation = route.Location;
            var bytes = Encoding.UTF8.GetBytes(route.Body);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            ctx.Response.Close();
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }
}
