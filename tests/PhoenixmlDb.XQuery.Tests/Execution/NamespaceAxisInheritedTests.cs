using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Regression: the <c>namespace::</c> axis must yield one namespace node per in-scope namespace
/// (XDM §6.2), and those nodes must survive the document-order deduplication that a path step
/// applies afterwards. Namespace nodes are synthesised (not physically stored), so if they are all
/// created with the same identity (<c>NodeId.None</c>, <c>TreeOrdinal 0</c>) their
/// <c>DocumentOrderKey</c> collides and the post-step dedup collapses every namespace node of an
/// element into one — so <c>element/namespace::*</c> reports only the first namespace (typically
/// the default) even though the element has several in scope. This drove the W3C XSLT insn/copy
/// failures copy-0616/0617/0618/0619/0624/0625/0626/0627/1220 (which iterate the namespace axis),
/// while their copy-0612/0620 siblings using <c>fn:in-scope-prefixes</c> (which returns strings,
/// not nodes) passed. The axis must therefore agree with <c>fn:in-scope-prefixes</c>.
/// </summary>
public class NamespaceAxisInheritedTests
{
    private const string Nested =
        "<a:x xmlns:a=\"urn:a\" xmlns:b=\"urn:b\"><c/></a:x>";

    private static async Task<string> Eval(string query, string inputXml)
    {
        var parser = new XQueryParserFacade { AllowNamespaceAxis = true };
        var expr = parser.Parse(query);
        var store = new XdmDocumentStore();
        var doc = store.LoadFromString(inputXml, "urn:test");
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var sb = new StringBuilder();
        await foreach (var item in engine.ExecuteAsync(expr, initialContextItem: doc))
            sb.Append(item?.ToString());
        return sb.ToString();
    }

    [Fact]
    public async Task NamespaceAxis_over_inherited_element_survives_document_order_dedup()
    {
        // c inherits a and b from its ancestor and additionally has the implicit xml namespace:
        // three in-scope namespace nodes. A bare path step deduplicates by DocumentOrderKey.
        var count = await Eval("count(//*:c/namespace::*)", Nested);
        count.Should().Be("3",
            "the namespace:: axis must yield a distinct node per in-scope namespace (a, b, xml), " +
            "not collapse them to one under document-order deduplication");
    }

    [Fact]
    public async Task NamespaceAxis_prefixes_match_InScopePrefixes()
    {
        // The prefix set surfaced through the namespace axis (via a path step + dedup) must equal
        // the prefix set returned by fn:in-scope-prefixes.
        var axis = await Eval(
            "string-join(sort(//*:c/namespace::*/name()), '|')", Nested);
        var isp = await Eval(
            "string-join(sort(in-scope-prefixes(//*:c)), '|')", Nested);
        axis.Should().Be(isp);
        axis.Should().Be("a|b|xml");
    }

    [Fact]
    public async Task NamespaceAxis_nodes_have_distinct_identities()
    {
        // Directly assert the operator gives each namespace node a distinct DocumentOrderKey — the
        // property that lets them survive dedup. (Guards the fix at the source.)
        var a = new XdmElement
        {
            Id = new NodeId(1),
            Document = new DocumentId(1),
            TreeOrdinal = 5,
            Namespace = NamespaceId.None,
            LocalName = "a",
            Attributes = XdmElement.EmptyAttributes,
            Children = ImmutableArray<NodeId>.Empty,
            NamespaceDeclarations = ImmutableArray.Create(
                new NamespaceBinding("p", new NamespaceId(11)),
                new NamespaceBinding("q", new NamespaceId(12))),
        };
        var provider = new DelegateNodeProvider(id => id == new NodeId(1) ? a : null);
        using var context = new QueryExecutionContext(
            ContainerId.None, nodeProvider: provider,
            namespaceResolver: ns => ns.Value == 11 ? "urn:p" : ns.Value == 12 ? "urn:q" : null);

        var keys = AxisNavigationOperator.GetNamespaceNodesStatic(a, context)
            .Select(n => n.DocumentOrderKey)
            .ToArray();

        keys.Should().OnlyHaveUniqueItems("each namespace node needs a distinct identity to survive document-order dedup");
        keys.Should().HaveCount(3); // p, q, xml
    }
}
