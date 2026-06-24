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
}
