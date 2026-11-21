using System.Text.RegularExpressions;
using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// <c>xs</c> is predeclared, not reserved: a query may bind it to another namespace (only
/// <c>xml</c> and <c>xmlns</c> may not be bound, XQST0070), and then <c>xs:integer</c> is
/// <c>Q{urn:x}integer</c>, no built-in type. Type names were taken for XML Schema types by their
/// prefix alone, so <c>declare namespace xs = "urn:x"; 1 instance of xs:integer</c> was true
/// (BUGS.md #135; Lucas, 3.3.0). A namespace declaration on a direct element constructor shadows
/// the prolog's binding as well.
/// </summary>
public class XsPrefixReboundTests
{
    private static async Task<string> Run(string query)
    {
        try
        {
            var store = new XdmDocumentStore();
            var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
            var compiled = engine.Compile(query);
            if (!compiled.Success)
                return Code(string.Join("; ", compiled.Errors));
            var items = new List<string>();
            await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
                items.Add(i?.ToString() ?? "");
            return string.Join(",", items);
        }
#pragma warning disable CA1031 // the error code is the result under test
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return ex is XQueryRuntimeException rt ? rt.ErrorCode : Code(ex.Message);
        }
    }

    private static string Code(string message) =>
        Regex.Match(message, @"\b(XP|XQ|FO)[A-Z]{2}\d{4}\b") is { Success: true } m ? m.Value : message;

    private const string Rebound = "declare namespace xs = 'urn:x'; ";

    [Theory]
    [InlineData(Rebound + "1 instance of xs:integer", "XPST0051")]
    [InlineData(Rebound + "'1' cast as xs:integer", "XQST0052")]
    [InlineData(Rebound + "<a/> instance of element(*, xs:untyped)", "XPST0008")]
    [InlineData(Rebound + "declare variable $v as xs:integer := 3; $v", "XPST0051")]
    [InlineData(Rebound + "declare function local:f($a as xs:string) { $a }; local:f('s')", "XPST0051")]
    [InlineData("string(<a xmlns:xs='urn:x'>{1 instance of xs:integer}</a>)", "XPST0051")]
    public async Task Xs_bound_to_another_namespace_names_no_built_in_type(string query, string expected)
        => (await Run(query)).Should().Be(expected);

    [Theory]
    [InlineData("1 instance of xs:integer", "True")]
    [InlineData("'1' cast as xs:integer", "1")]
    [InlineData("<a/> instance of element(*, xs:untyped)", "True")]
    [InlineData("1 instance of xsd:integer", "True")]
    [InlineData("declare namespace p = 'http://www.w3.org/2001/XMLSchema'; 1 instance of p:integer", "True")]
    [InlineData("declare namespace xs = 'http://www.w3.org/2001/XMLSchema'; 1 instance of xs:integer", "True")]
    [InlineData("string(<a xmlns:p='http://www.w3.org/2001/XMLSchema'>{1 instance of p:integer}</a>)", "true")]
    public async Task Xs_and_other_prefixes_bound_to_xml_schema_name_built_in_types(string query, string expected)
        => (await Run(query)).Should().Be(expected);
}
