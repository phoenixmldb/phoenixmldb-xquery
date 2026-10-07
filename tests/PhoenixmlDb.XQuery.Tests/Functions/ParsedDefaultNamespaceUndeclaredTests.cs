using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Functions;

/// <summary>
/// An element parsed with <c>xmlns=""</c> has no default namespace in scope (xquery#105).
/// <c>fn:parse-xml</c> did not record the undeclaration, so <c>in-scope-prefixes()</c> reported
/// the parent's default namespace as in scope.
/// The facade output of such a tree must keep <c>xmlns=""</c>, or a parser reading it back puts
/// the element in the parent's namespace.
/// </summary>
public sealed class ParsedDefaultNamespaceUndeclaredTests
{
    private const string Parsed = "parse-xml('<r xmlns=\"urn:d\" xmlns:p=\"urn:p\"><z xmlns=\"\"><k/></z><y/></r>')";

    private static Task<string> Run(string query) => new XQueryFacade().EvaluateAsync(query);

    [Theory]
    [InlineData("/*/*:z", "p,xml")]
    [InlineData("/*/*:z/*:k", "p,xml")]
    [InlineData("/*/*:y", ",p,xml")]
    [InlineData("/*", ",p,xml")]
    public async Task In_scope_prefixes_leave_out_an_undeclared_default(string path, string expected) =>
        (await Run($"string-join(sort(in-scope-prefixes({Parsed}{path})), ',')")).Should().Be(expected);

    [Theory]
    [InlineData("/*/*:z", "0")]
    [InlineData("/*/*:z/*:k", "0")]
    [InlineData("/*/*:y", "1")]
    public async Task The_default_namespace_has_no_uri_under_the_undeclaration(string path, string expected) =>
        (await Run($"count(namespace-uri-for-prefix('', {Parsed}{path}))")).Should().Be(expected);

    [Fact]
    public async Task The_element_and_its_children_are_in_no_namespace() =>
        (await Run($"string-join({Parsed}//*[local-name() = ('z', 'k', 'y')] ! ('[' || namespace-uri(.) || ']'), '')"))
            .Should().Be("[][][urn:d]");

    [Fact]
    public async Task The_facade_output_keeps_the_undeclaration()
    {
        var output = await Run(Parsed + "/*");
        output.Should().Contain("<z xmlns=\"\">");
        // What matters is what the output means: read back, z and k are still in no namespace.
        var reread = System.Xml.Linq.XElement.Parse(output);
        reread.Elements().Select(e => e.Name.NamespaceName).Should().Equal("", "urn:d");
        reread.Elements().First().Elements().Single().Name.NamespaceName.Should().BeEmpty();
    }

    [Fact]
    public async Task Serialize_writes_the_element_in_no_namespace()
    {
        (await Run($"serialize({Parsed}/*/*:z)")).Should().Be("<z xmlns:p=\"urn:p\"><k/></z>");
        (await Run($"serialize({Parsed}/*)")).Should().Contain("<z xmlns=\"\"><k/></z>");
    }
}
