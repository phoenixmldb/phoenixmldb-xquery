using System.Collections.Immutable;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// Regression tests for #160: implicit atomization (fn:number, fn:data, type-constructor
/// argument atomization) of elements deserialized from storage.
///
/// Storage-deserialized elements carry a NULL precomputed string value — their string value
/// is only computable by walking descendant text nodes via an <see cref="INodeProvider"/>.
/// fn:string() already handled this correctly because it routed through the provider-aware
/// atomize path. The implicit atomization sites used the parameterless (null-provider) path,
/// so they saw empty text → NaN / empty. These tests assert the implicit paths now thread the
/// execution context's node provider, exactly as fn:string() does.
/// </summary>
public class ProviderBackedAtomizationTests
{
    private const ulong ElemId = 1;
    private const ulong TextId = 2;

    /// <summary>
    /// Builds a provider-backed element <c>&lt;value&gt;42.5&lt;/value&gt;</c> whose
    /// precomputed StringValue is NULL (mimicking a storage-deserialized node). The text content
    /// lives in a child text node resolvable ONLY through the fake node provider, and the
    /// execution context is constructed with that provider — mirroring the real engine.
    /// </summary>
    private static (XdmElement Element, QueryExecutionContext Context) BuildProviderBackedElement(string text)
    {
        var doc = DocumentId.None;

        var textNode = new XdmText
        {
            Id = new NodeId(TextId),
            Document = doc,
            Value = text,
        };

        var element = new XdmElement
        {
            Id = new NodeId(ElemId),
            Document = doc,
            Namespace = NamespaceId.None,
            LocalName = "value",
            Attributes = XdmElement.EmptyAttributes,
            Children = ImmutableArray.Create(new NodeId(TextId)),
            NamespaceDeclarations = ImmutableArray<NamespaceBinding>.Empty,
        };

        // Precomputed string value is NULL — the engine must walk the provider to atomize.
        element.StringValue.Should().BeEmpty("storage-deserialized elements have no precomputed string value");

        var provider = new DelegateNodeProvider(id =>
            id == new NodeId(TextId) ? textNode :
            id == new NodeId(ElemId) ? element : null);

        var context = new QueryExecutionContext(ContainerId.None, nodeProvider: provider);
        return (element, context);
    }

    [Fact]
    public async Task Number_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("42.5");

        var result = await new NumberFunction().InvokeAsync([element], context);

        result.Should().Be(42.5);
    }

    [Fact]
    public async Task Data_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("42.5");

        var result = await new DataFunction().InvokeAsync([element], context);

        // fn:data yields xs:untypedAtomic whose lexical value is the element's string value.
        result.Should().BeOfType<PhoenixmlDb.Xdm.XsUntypedAtomic>()
            .Which.Value.Should().Be("42.5");
    }

    [Fact]
    public async Task DecimalConstructor_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("42.5");

        var result = await new DecimalConstructorFunction().InvokeAsync([element], context);

        result.Should().Be(42.5m);
    }

    // ---- #163: string functions reading element string value via implicit atomization ----

    /// <summary>
    /// Builds a second provider-backed element with a distinct id so two elements (plus their
    /// text children) are simultaneously resolvable through one provider.
    /// </summary>
    private static (XdmElement A, XdmElement B, QueryExecutionContext Context) BuildTwoProviderBackedElements(
        string textA, string textB)
    {
        var doc = DocumentId.None;
        const ulong elemAId = 10, textAId = 11, elemBId = 20, textBId = 21;

        var textNodeA = new XdmText { Id = new NodeId(textAId), Document = doc, Value = textA };
        var textNodeB = new XdmText { Id = new NodeId(textBId), Document = doc, Value = textB };

        var elemA = new XdmElement
        {
            Id = new NodeId(elemAId),
            Document = doc,
            Namespace = NamespaceId.None,
            LocalName = "value",
            Attributes = XdmElement.EmptyAttributes,
            Children = ImmutableArray.Create(new NodeId(textAId)),
            NamespaceDeclarations = ImmutableArray<NamespaceBinding>.Empty,
        };
        var elemB = new XdmElement
        {
            Id = new NodeId(elemBId),
            Document = doc,
            Namespace = NamespaceId.None,
            LocalName = "value",
            Attributes = XdmElement.EmptyAttributes,
            Children = ImmutableArray.Create(new NodeId(textBId)),
            NamespaceDeclarations = ImmutableArray<NamespaceBinding>.Empty,
        };

        elemA.StringValue.Should().BeEmpty();
        elemB.StringValue.Should().BeEmpty();

        var provider = new DelegateNodeProvider(id =>
            id == new NodeId(textAId) ? textNodeA :
            id == new NodeId(textBId) ? textNodeB :
            id == new NodeId(elemAId) ? elemA :
            id == new NodeId(elemBId) ? elemB : null);

        var context = new QueryExecutionContext(ContainerId.None, nodeProvider: provider);
        return (elemA, elemB, context);
    }

    [Fact]
    public async Task Contains_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("hello");
        var result = await new ContainsFunction().InvokeAsync([element, "ell"], context);
        result.Should().Be(true);
    }

    [Fact]
    public async Task StartsWith_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("hello");
        var result = await new StartsWithFunction().InvokeAsync([element, "he"], context);
        result.Should().Be(true);
    }

    [Fact]
    public async Task EndsWith_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("hello");
        var result = await new EndsWithFunction().InvokeAsync([element, "lo"], context);
        result.Should().Be(true);
    }

    [Fact]
    public async Task StringLength_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("hello");
        var result = await new StringLengthFunction().InvokeAsync([element], context);
        result.Should().Be(5L);
    }

    [Fact]
    public async Task UpperCase_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("abc");
        var result = await new UpperCaseFunction().InvokeAsync([element], context);
        result.Should().Be("ABC");
    }

    [Fact]
    public async Task LowerCase_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("ABC");
        var result = await new LowerCaseFunction().InvokeAsync([element], context);
        result.Should().Be("abc");
    }

    [Fact]
    public async Task NormalizeSpace_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("  a   b  ");
        var result = await new NormalizeSpaceFunction().InvokeAsync([element], context);
        result.Should().Be("a b");
    }

    [Fact]
    public async Task Tokenize_ProviderBackedElement_AtomizesViaProvider()
    {
        var (element, context) = BuildProviderBackedElement("a,b,c");
        var result = await new TokenizeFunction().InvokeAsync([element, ","], context);
        result.Should().BeOfType<string[]>().Which.Should().Equal("a", "b", "c");
    }

    [Fact]
    public async Task StringJoin_ProviderBackedElements_AtomizeViaProvider()
    {
        var (a, b, context) = BuildTwoProviderBackedElements("a", "b");
        var result = await new StringJoinFunction().InvokeAsync([new object?[] { a, b }, "-"], context);
        result.Should().Be("a-b");
    }

    [Fact]
    public async Task DistinctValues_ProviderBackedElements_AtomizeViaProvider()
    {
        var (a, b, context) = BuildTwoProviderBackedElements("x", "y");
        var result = await new DistinctValuesFunction().InvokeAsync([new object?[] { a, a, b }], context);
        result.Should().BeOfType<object?[]>().Which.Should().Equal("x", "y");
    }
}
