using System.Collections.Immutable;
using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// The contract with a storage layer (phoenixmldb-xquery#5): an element reconstructed from
/// storage carries a <see cref="XdmNode.StringValueResolver"/>, and with it every implicit
/// atomization path gives the right answer.
/// </summary>
/// <remarks>
/// Without a resolver, about 89 atomization sites read the element's cached string value and saw
/// <c>''</c>: comparisons, arithmetic, casts, switch, rounding, map keys. Three sweeps threaded the
/// node provider through some of them (#160, #163, the aggregates). The resolver is the complete
/// fix and Core's supported hook (phoenixmldb-core#4); the storage side is endpointsystems/phoenixml#49.
/// These tests pin the XQuery half: given a resolver, nothing may still read <c>''</c>.
/// </remarks>
public sealed class ResolverBackedAtomizationTests
{
    private static async Task<string> EvaluateAsync(string query)
    {
        var text = new XdmText { Id = new NodeId(2), Document = DocumentId.None, Value = "42.5" };
        var element = new XdmElement
        {
            Id = new NodeId(1),
            Document = DocumentId.None,
            Namespace = NamespaceId.None,
            LocalName = "value",
            Attributes = XdmElement.EmptyAttributes,
            Children = ImmutableArray.Create(new NodeId(2)),
            NamespaceDeclarations = ImmutableArray<NamespaceBinding>.Empty,
            // What a storage layer supplies: the descendant text, computed on first read.
            StringValueResolver = _ => text.Value,
        };
        var provider = new DelegateNodeProvider(id =>
            id == new NodeId(2) ? text : id == new NodeId(1) ? element : null);

        var engine = new QueryEngine(nodeProvider: provider);
        var compiled = engine.Compile("declare variable $e external; " + query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var context = engine.CreateContext();
        context.SetExternalVariable("e", element);
        var items = new List<string>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(context))
            items.Add(item is XdmNode ? "node" : Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture) ?? "");
        return string.Join("|", items);
    }

    [Theory]
    [InlineData("$e > 20", "True")]
    [InlineData("$e * 2", "85")]
    [InlineData("$e + 0", "42.5")]
    [InlineData("$e = '42.5'", "True")]
    [InlineData("$e eq '42.5'", "True")]
    [InlineData("-$e", "-42.5")]
    [InlineData("$e idiv 2", "21")]
    [InlineData("$e mod 5", "2.5")]
    [InlineData("$e cast as xs:double", "42.5")]
    [InlineData("$e castable as xs:double", "True")]
    [InlineData("switch ($e) case '42.5' return 'y' default return 'n'", "y")]
    [InlineData("format-number($e, '0.0')", "42.5")]
    [InlineData("round($e)", "43")]
    [InlineData("floor($e)", "42")]
    [InlineData("abs($e)", "42.5")]
    [InlineData("index-of(($e), '42.5')", "1")]
    [InlineData("map:keys(map:put(map{}, $e, 1))", "42.5")]
    [InlineData("map:keys(map:entry($e, 1))", "42.5")]
    [InlineData("array:index-of([$e], '42.5')", "1")]
    [InlineData("contains-subsequence(($e), ('42.5'))", "True")]
    [InlineData("every $x in $e satisfies $x > 1", "True")]
    [InlineData("sum(($e, $e))", "85")]
    [InlineData("max(($e, 1))", "42.5")]
    public async Task A_resolver_backed_element_atomizes_to_its_text(string query, string expected)
        => (await EvaluateAsync(query)).Should().Be(expected);

    /// <summary>fn:atomic-equal is op:same-key: xs:untypedAtomic equals the same xs:string.</summary>
    [Theory]
    [InlineData("atomic-equal($e, '42.5')", "True")]
    [InlineData("atomic-equal(xs:untypedAtomic('a'), 'a')", "True")]
    [InlineData("atomic-equal(1, 1.0)", "True")]
    [InlineData("atomic-equal(1, '1')", "False")]
    [InlineData("atomic-equal('a', 'b')", "False")]
    public async Task Atomic_equal_is_same_key(string query, string expected)
        => (await EvaluateAsync(query)).Should().Be(expected);

    /// <summary>An untypedAtomic range operand that is not an integer raises FORG0001, not a .NET exception.</summary>
    [Theory]
    [InlineData("$e to 3")]
    [InlineData("xs:untypedAtomic('x') to 3")]
    public async Task A_non_integer_range_operand_raises_FORG0001(string query)
    {
        var act = async () => await EvaluateAsync(query);
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FORG0001");
    }
}
