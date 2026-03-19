using System.Text;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.Xdm.Nodes;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// Tests for dbxml:metadata() functions.
/// </summary>
public class MetadataFunctionTests
{
    private static readonly DocumentId TestDocId = new(42);

    #region MetadataGetFunction Tests

    [Fact]
    public async Task MetadataGet_UserKey_ReturnsValue()
    {
        var func = new MetadataGetFunction();
        var node = CreateTestElement();
        var context = CreateContextWithMetadata(
            resolver: (docId, key) => key == "author" ? Encoding.UTF8.GetBytes("Alice") : null);

        var result = await func.InvokeAsync([node, "author"], context);

        result.Should().Be("Alice");
    }

    [Fact]
    public async Task MetadataGet_MissingKey_ReturnsNull()
    {
        var func = new MetadataGetFunction();
        var node = CreateTestElement();
        var context = CreateContextWithMetadata(
            resolver: (_, _) => null);

        var result = await func.InvokeAsync([node, "nonexistent"], context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task MetadataGet_NullNode_ReturnsNull()
    {
        var func = new MetadataGetFunction();
        var context = CreateContextWithMetadata();

        var result = await func.InvokeAsync([null, "author"], context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task MetadataGet_SystemKey_Name_ReturnsValue()
    {
        var func = new MetadataGetFunction();
        var node = CreateTestElement();
        var context = CreateContextWithMetadata(
            resolver: (docId, key) => key == "dbxml:name" ? Encoding.UTF8.GetBytes("product.xml") : null);

        var result = await func.InvokeAsync([node, "dbxml:name"], context);

        result.Should().Be("product.xml");
    }

    [Fact]
    public async Task MetadataGet_SystemKey_Size_ReturnsNumeric()
    {
        var func = new MetadataGetFunction();
        var node = CreateTestElement();
        var context = CreateContextWithMetadata(
            resolver: (docId, key) => key == "dbxml:size" ? Encoding.UTF8.GetBytes("1024") : null);

        var result = await func.InvokeAsync([node, "dbxml:size"], context);

        result.Should().Be(1024L);
    }

    [Fact]
    public async Task MetadataGet_SystemKey_NodeCount_ReturnsNumeric()
    {
        var func = new MetadataGetFunction();
        var node = CreateTestElement();
        var context = CreateContextWithMetadata(
            resolver: (docId, key) => key == "dbxml:node-count" ? Encoding.UTF8.GetBytes("50") : null);

        var result = await func.InvokeAsync([node, "dbxml:node-count"], context);

        result.Should().Be(50L);
    }

    [Fact]
    public async Task MetadataGet_SystemKey_ContentType_ReturnsString()
    {
        var func = new MetadataGetFunction();
        var node = CreateTestElement();
        var context = CreateContextWithMetadata(
            resolver: (docId, key) => key == "dbxml:content-type" ? Encoding.UTF8.GetBytes("application/xml") : null);

        var result = await func.InvokeAsync([node, "dbxml:content-type"], context);

        result.Should().Be("application/xml");
    }

    [Fact]
    public async Task MetadataGet_SystemKey_Created_ReturnsString()
    {
        var func = new MetadataGetFunction();
        var node = CreateTestElement();
        var context = CreateContextWithMetadata(
            resolver: (docId, key) => key == "dbxml:created" ? Encoding.UTF8.GetBytes("2024-01-15T10:30:00Z") : null);

        var result = await func.InvokeAsync([node, "dbxml:created"], context);

        result.Should().Be("2024-01-15T10:30:00Z");
    }

    [Fact]
    public async Task MetadataGet_NoResolver_ReturnsNull()
    {
        var func = new MetadataGetFunction();
        var node = CreateTestElement();
        var context = new QueryExecutionContext(new ContainerId(1));

        var result = await func.InvokeAsync([node, "author"], context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task MetadataGet_PassesCorrectDocumentId()
    {
        var func = new MetadataGetFunction();
        var node = CreateTestElement();
        DocumentId capturedDocId = default;
        var context = CreateContextWithMetadata(
            resolver: (docId, key) =>
            {
                capturedDocId = docId;
                return Encoding.UTF8.GetBytes("value");
            });

        await func.InvokeAsync([node, "key"], context);

        capturedDocId.Should().Be(TestDocId);
    }

    [Fact]
    public void MetadataGet_Name_IsDbxmlMetadata()
    {
        var func = new MetadataGetFunction();

        func.Name.LocalName.Should().Be("metadata");
        func.Name.Namespace.Should().Be(FunctionNamespaces.Dbxml);
    }

    [Fact]
    public void MetadataGet_Arity_IsTwo()
    {
        var func = new MetadataGetFunction();

        func.Arity.Should().Be(2);
    }

    #endregion

    #region MetadataAllFunction Tests

    [Fact]
    public async Task MetadataAll_ReturnsAllEntries()
    {
        var func = new MetadataAllFunction();
        var node = CreateTestElement();
        var context = CreateContextWithMetadata(
            allResolver: _ =>
            [
                ("author", Encoding.UTF8.GetBytes("Alice")),
                ("version", Encoding.UTF8.GetBytes("2.0"))
            ]);

        var result = await func.InvokeAsync([node], context) as IDictionary<object, object?>;

        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result["author"].Should().Be("Alice");
        result["version"].Should().Be("2.0");
    }

    [Fact]
    public async Task MetadataAll_EmptyMetadata_ReturnsEmptyMap()
    {
        var func = new MetadataAllFunction();
        var node = CreateTestElement();
        var context = CreateContextWithMetadata(
            allResolver: _ => []);

        var result = await func.InvokeAsync([node], context) as IDictionary<object, object?>;

        result.Should().NotBeNull();
        result!.Count.Should().Be(0);
    }

    [Fact]
    public async Task MetadataAll_NullNode_ReturnsEmptyMap()
    {
        var func = new MetadataAllFunction();
        var context = CreateContextWithMetadata();

        var result = await func.InvokeAsync([null], context) as IDictionary<object, object?>;

        result.Should().NotBeNull();
        result!.Count.Should().Be(0);
    }

    [Fact]
    public async Task MetadataAll_NoResolver_ReturnsEmptyMap()
    {
        var func = new MetadataAllFunction();
        var node = CreateTestElement();
        var context = new QueryExecutionContext(new ContainerId(1));

        var result = await func.InvokeAsync([node], context) as IDictionary<object, object?>;

        result.Should().NotBeNull();
        result!.Count.Should().Be(0);
    }

    [Fact]
    public async Task MetadataAll_PassesCorrectDocumentId()
    {
        var func = new MetadataAllFunction();
        var node = CreateTestElement();
        DocumentId capturedDocId = default;
        var context = CreateContextWithMetadata(
            allResolver: docId =>
            {
                capturedDocId = docId;
                return [];
            });

        await func.InvokeAsync([node], context);

        capturedDocId.Should().Be(TestDocId);
    }

    [Fact]
    public void MetadataAll_Name_IsDbxmlMetadata()
    {
        var func = new MetadataAllFunction();

        func.Name.LocalName.Should().Be("metadata");
        func.Name.Namespace.Should().Be(FunctionNamespaces.Dbxml);
    }

    [Fact]
    public void MetadataAll_Arity_IsOne()
    {
        var func = new MetadataAllFunction();

        func.Arity.Should().Be(1);
    }

    #endregion

    #region FunctionLibrary Registration Tests

    [Fact]
    public void FunctionLibrary_ContainsMetadataGet()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Dbxml, "metadata"), 2);

        func.Should().NotBeNull();
        func.Should().BeOfType<MetadataGetFunction>();
    }

    [Fact]
    public void FunctionLibrary_ContainsMetadataAll()
    {
        var lib = FunctionLibrary.Standard;
        var func = lib.Resolve(new QName(FunctionNamespaces.Dbxml, "metadata"), 1);

        func.Should().NotBeNull();
        func.Should().BeOfType<MetadataAllFunction>();
    }

    #endregion

    #region Helper Methods

    private static XdmElement CreateTestElement()
    {
        return new XdmElement
        {
            Id = new NodeId(1),
            Document = TestDocId,
            Namespace = NamespaceId.None,
            LocalName = "product",
            Attributes = [],
            Children = [],
            NamespaceDeclarations = []
        };
    }

    private static QueryExecutionContext CreateContextWithMetadata(
        Func<DocumentId, string, byte[]?>? resolver = null,
        Func<DocumentId, IEnumerable<(string Key, byte[] Value)>>? allResolver = null)
    {
        IMetadataProvider? metadataProvider = null;
        if (resolver != null || allResolver != null)
        {
            metadataProvider = new DelegateMetadataProvider(
                resolver ?? ((_, _) => null),
                allResolver ?? (_ => []));
        }

        return new QueryExecutionContext(
            new ContainerId(1),
            metadataProvider: metadataProvider);
    }

    #endregion
}
