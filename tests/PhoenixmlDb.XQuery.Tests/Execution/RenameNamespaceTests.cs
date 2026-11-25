using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// <c>rename node</c> puts the node in the new name's namespace, with its prefix (BUGS.md #129).
/// </summary>
/// <remarks>
/// fn:QName made the name's namespace id from the URI's hash, an id no store knows, and the
/// rename passed only that id and the local name on: the renamed node had no namespace
/// (<c>namespace-uri()</c> was "") and the serializer refused it. The node also kept its OLD
/// prefix, a rename of the copy's root did nothing, and a string name with a prefix was kept as
/// one local name with a colon.
/// </remarks>
public class RenameNamespaceTests
{
    private readonly XQueryFacade _facade = new();

    // Each row: the update on $c := copy of <e a="1"><k>v</k></e>, the path to the renamed node,
    // its expected namespace URI, and the expected serialization.
    [Theory]
    [InlineData("""rename node $c/k as QName("urn:a","p:k")""", "$r/*", "urn:a", """<e a="1"><p:k xmlns:p="urn:a">v</p:k></e>""")]
    [InlineData("""rename node $c/k as QName("urn:a","k2")""", "$r/*", "urn:a", """<e a="1"><k2 xmlns="urn:a">v</k2></e>""")]
    [InlineData("""rename node $c/@a as QName("urn:a","p:a")""", "$r/@*", "urn:a", """<e p:a="1" xmlns:p="urn:a"><k>v</k></e>""")]
    [InlineData("""rename node $c as QName("urn:a","p:x")""", "$r", "urn:a", """<p:x a="1" xmlns:p="urn:a"><k>v</k></p:x>""")]
    [InlineData("""rename node $c as "x" """, "$r", "", """<x a="1"><k>v</k></x>""")]
    [InlineData("""rename node $c/k as "q:k" """, "$r/*", "urn:q", """<e a="1"><q:k xmlns:q="urn:q">v</q:k></e>""")]
    [InlineData("""rename node $c/k as xs:untypedAtomic("q:k")""", "$r/*", "urn:q", """<e a="1"><q:k xmlns:q="urn:q">v</q:k></e>""")]
    public async Task Renamed_node_is_in_the_new_namespace(string update, string path, string ns, string serialized)
    {
        var query = $$"""
            declare namespace q = "urn:q";
            let $r := copy $c := <e a="1"><k>v</k></e> modify ({{update}}) return $c
            return string(namespace-uri({{path}})) || "|" || serialize($r)
            """;
        (await _facade.EvaluateAsync(query)).Should().Be($"{ns}|{serialized}");
    }

    [Theory]
    [InlineData("""<e xmlns:p="urn:x"><k/></e>""", """rename node $c/k as QName("urn:a","p:k")""")]
    [InlineData("""<e xmlns:p="urn:x" a="1"/>""", """rename node $c/@a as QName("urn:a","p:a")""")]
    public async Task Clashing_prefix_is_XUDY0023(string source, string update)
    {
        // The new name binds p to urn:a where the element's namespaces (for an attribute, its
        // parent's) bind p to urn:x: XQUF 3.0 §5.4 raises XUDY0023. It waited for the ruling of
        // BUGS.md #128 ("raise every error XQUF 3.0 requires"); until then the node was renamed and
        // the serializer chose another prefix.
        var act = () => _facade.EvaluateAsync($$"""copy $c := {{source}} modify ({{update}}) return $c""");
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0023");
    }

    [Fact]
    public async Task Unbound_prefix_in_a_string_name_is_XQDY0074()
    {
        var act = () => _facade.EvaluateAsync("""copy $c := <e><k/></e> modify rename node $c/k as "z:k" return $c""");
        (await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XQDY0074");
    }
}
