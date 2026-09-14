using System.Collections.Immutable;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Valid queries must not trip Core's StrictStringValue. The engine asked nodes for their public
/// StringValue before computing it; with no cached value and no resolver that read throws under
/// strict, before the computation it guarded could run (phoenixmldb-xquery#37).
/// </summary>
public class StrictStringValueTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task Count_of_a_constructed_document()
        => (await _facade.EvaluateAsync("count(document{ <e>x</e> })", "<x/>")).Should().Be("1");

    [Fact]
    public async Task String_of_a_constructed_document()
        => (await _facade.EvaluateAsync("string(document{ <e>x</e> })", "<x/>")).Should().Be("x");

    [Fact]
    public void A_provider_backed_element_computes_its_string_value()
    {
        var text = new XdmText { Id = new NodeId(2), Document = DocumentId.None, Value = "42.5" };
        var elem = new XdmElement
        {
            Id = new NodeId(1), Document = DocumentId.None, Namespace = NamespaceId.None, LocalName = "value",
            Attributes = XdmElement.EmptyAttributes, Children = ImmutableArray.Create(new NodeId(2)),
            NamespaceDeclarations = ImmutableArray<NamespaceBinding>.Empty,
        };
        var provider = new DelegateNodeProvider(id => id == new NodeId(2) ? text : null);
        QueryExecutionContext.ComputeElementStringValue(elem, provider).Should().Be("42.5");
    }
}
