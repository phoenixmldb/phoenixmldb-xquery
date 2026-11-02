using System.Net;
using System.Net.Sockets;
using System.Text;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// A schema location that cannot be fetched is XQST0059, however the fetch fails. On a network
/// that drops packets the fetch times out: QT3 schema-import-1 ran 100 s with the provider's lock
/// held, then left Compile as a raw TaskCanceledException.
/// </summary>
public sealed class SchemaFetchFailureTests
{
    private sealed class TimesOut : ResourceResolverBase
    {
        public override ResourceContent? ResolveContent(ResourceRequest request) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
    }

    [Fact]
    public void A_timed_out_fetch_is_XQST0059()
    {
        var policy = ResourcePolicy.CreateBuilder().WithResourceResolver(new TimesOut()).Build();

        var import = () => new XsdSchemaProvider().ImportSchema("urn:s", ["http://example.invalid/s.xsd"], policy);

        import.Should().Throw<SchemaException>().Which.Message.Should().Contain("urn:s");
    }

    [Fact]
    public async Task Without_a_policy_the_fetch_uses_the_engines_bounded_client()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var request = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer);
            await stream.WriteAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
            return Encoding.ASCII.GetString(buffer, 0, read);
        });

        var import = () => new XsdSchemaProvider().ImportSchema("urn:s", [$"http://127.0.0.1:{port}/s.xsd"]);

        import.Should().Throw<SchemaException>();
        (await request).Should().Contain("User-Agent: PhoenixmlDb.XQuery");
    }
}
