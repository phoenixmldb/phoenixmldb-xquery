using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Direct constructors inside the copy, modify and return clauses of a transform expression
/// resolve their names like any other constructor (BUGS.md #130).
/// </summary>
/// <remarks>
/// The expression rewriter had no case for a transform expression, so no rewrite pass (namespace
/// resolution among them) entered its clauses. A prefixed or default-namespace element built
/// there was put in no namespace with its prefix kept: <c>namespace-uri()</c> returned "" and the
/// serializer failed or wrote <c>urn:unresolved:q</c>. Present in 3.2.0.
/// </remarks>
public class TransformClauseNamespaceTests
{
    private readonly XQueryFacade _facade = new();

    [Theory]
    [InlineData("copy $c := <e xmlns:q=\"urn:q\"><q:m/></e> modify () return $c", "urn:q", """<e xmlns:q="urn:q"><q:m/></e>""")]
    [InlineData("copy $c := <e/> modify insert node <q:m xmlns:q=\"urn:q\"/> into $c return $c", "urn:q", """<e><q:m xmlns:q="urn:q"/></e>""")]
    [InlineData("copy $c := <e/> modify () return <w><q:m xmlns:q=\"urn:q\"/></w>", "urn:q", """<w><q:m xmlns:q="urn:q"/></w>""")]
    [InlineData("copy $c := <e xmlns=\"urn:d\"><m/></e> modify () return $c", "urn:d", """<e xmlns="urn:d"><m/></e>""")]
    [InlineData("copy $c := <e xmlns:q=\"urn:q\" q:a=\"1\"/> modify () return $c", "", """<e xmlns:q="urn:q" q:a="1"/>""")]
    public async Task Constructor_in_a_transform_clause_keeps_its_namespace(string transform, string childNamespace, string serialized)
    {
        var query = $"let $r := ({transform}) return (string(namespace-uri($r/*)) || '|' || string-join($r/@*/namespace-uri(.), ',') || '|' || serialize($r))";
        var attrNs = transform.Contains("q:a=", StringComparison.Ordinal) ? "urn:q" : "";
        (await _facade.EvaluateAsync(query)).Should().Be($"{childNamespace}|{attrNs}|{serialized}");
    }
}
