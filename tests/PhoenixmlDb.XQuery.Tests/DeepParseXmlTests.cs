using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// fn:parse-xml converts without recursion, so a deeply nested document parses instead of
/// overflowing the stack, an uncatchable crash that took the whole process down (#102).
/// </summary>
public sealed class DeepParseXmlTests
{
    private readonly XQueryFacade _facade = new();

    private static string Deep(int depth, string attrs = "")
        => $"""parse-xml(string-join((for $i in 1 to {depth} return '<x{attrs}>', for $i in 1 to {depth} return '</x>')))""";

    [Fact]
    public async Task A_20000_deep_document_parses()
        => (await _facade.EvaluateAsync($"count({Deep(20000, " a=\"1\"")}/*)")).Should().Be("1");

    [Fact]
    public async Task The_deepest_element_keeps_its_attributes_and_text()
        => (await _facade.EvaluateAsync("""
               let $d := parse-xml(string-join((for $i in 1 to 3000 return '<x>', 'leaf', for $i in 1 to 3000 return '</x>')))
               return string($d)
               """)).Should().Be("leaf");

    [Fact]
    public async Task Namespaces_in_scope_are_unchanged_by_the_shared_scope()
        => (await _facade.EvaluateAsync("""
               let $d := parse-xml('<r xmlns="urn:d" xmlns:a="urn:a"><b:x xmlns:b="urn:b"><y a:k="1"><w xmlns:a="urn:a2"/></y></b:x><q/></r>')
               return string-join(for $e in $d//* return name($e) || '=' || namespace-uri-for-prefix('a', $e) || '|' || namespace-uri-for-prefix('b', $e), ' ')
               """)).Should().Be("r=urn:a| b:x=urn:a|urn:b y=urn:a|urn:b w=urn:a2|urn:b q=urn:a|");
}
