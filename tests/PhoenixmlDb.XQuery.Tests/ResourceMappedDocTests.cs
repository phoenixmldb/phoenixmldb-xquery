using FluentAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Verifies that <c>fn:doc</c> / <c>fn:json-doc</c> honor host-registered resource-URI
/// mappings: a relative URI that resolves (against the static base URI) to a logical
/// http:// URI bound to a local file is read from that file instead of triggering a
/// network fetch or being treated as a literal path. Regression guard for the QT3
/// app-Walmsley relative-doc() cluster.
/// </summary>
public sealed class ResourceMappedDocTests
{
    private static async Task<object?> EvaluateAsync(string query, string staticBaseUri,
        Dictionary<string, string> resourceMappings)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compilation = engine.Compile(query);
        compilation.Success.Should().BeTrue(
            string.Join("; ", compilation.Errors.Select(e => e.Message)));

        var context = engine.CreateContext();
        context.StaticBaseUri = staticBaseUri;
        context.SetResourceMappings(resourceMappings);

        object? result = null;
        await foreach (var item in compilation.ExecutionPlan!.ExecuteAsync(context))
        {
            result = item;
            break;
        }
        return result;
    }

    [Fact]
    public async Task Doc_resolves_relative_uri_through_resource_mapping()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phoenixmldb-doc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var xmlPath = Path.Combine(dir, "catalog.xml");
        await File.WriteAllTextAsync(xmlPath, "<catalog><product>557</product></catalog>");
        try
        {
            const string baseUri = "http://example.org/fots/Walmsley/";
            var mappings = new Dictionary<string, string>
            {
                [baseUri + "catalog.xml"] = xmlPath
            };

            var result = await EvaluateAsync(
                "string(doc('catalog.xml')//product)", baseUri, mappings);

            result?.ToString().Should().Be("557");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task JsonDoc_resolves_relative_uri_through_resource_mapping()
    {
        var dir = Path.Combine(Path.GetTempPath(), "phoenixmldb-json-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var jsonPath = Path.Combine(dir, "product.json");
        await File.WriteAllTextAsync(jsonPath, "{\"name\":\"Fleece Pullover\"}");
        try
        {
            const string baseUri = "http://example.org/fots/Walmsley/";
            var mappings = new Dictionary<string, string>
            {
                [baseUri + "product.json"] = jsonPath
            };

            var result = await EvaluateAsync(
                "json-doc('product.json')?name", baseUri, mappings);

            result?.ToString().Should().Be("Fleece Pullover");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
