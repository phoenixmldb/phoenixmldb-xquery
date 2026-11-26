using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The value of the <c>method</c> serialization parameter (Serialization 3.1 §3): an expanded QName; in
/// no namespace one of xml, xhtml, html, text, json and adaptive, as written; in a namespace an
/// implementation-defined method, and this processor defines none. A value that names no method is
/// "invalid for the given parameter", SEPM0016 (§3); in an <c>output:serialization-parameters</c>
/// element it makes the element invalid, SEPM0017 (§3.1). Before, the name was compared in lower case,
/// and a name that matched none was taken as adaptive with no error: <c>map{'method':'XML'}</c> gave
/// XML output, <c>'bogus'</c> adaptive output.
/// </summary>
public sealed class OutputMethodNameTests : IDisposable
{
    private const string OutputNs = "http://www.w3.org/2010/xslt-xquery-serialization";
    private const string Output = "declare namespace output = \"" + OutputNs + "\"; ";
    private readonly XQueryFacade _facade = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-method").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    public static TheoryData<string> NoMethod => new()
    {
        "XML",                           // the names are case-sensitive
        "Text",
        "bogus",
        "",
        "Q{http://example.com/m}m",      // a namespace: no implementation-defined method here
        "Q{http://www.w3.org/2010/xslt-xquery-serialization}xml",
    };

    // In a direct attribute, "{" starts an enclosed expression: Q{...} is written with "{{" and "}}".
    private static string Element(string method)
        => $"<output:serialization-parameters xmlns:output='{OutputNs}'><output:method value='{method.Replace("{", "{{").Replace("}", "}}")}'/></output:serialization-parameters>";

    private async Task<string> ErrorCodeOf(string query)
    {
        var act = () => _facade.EvaluateAsync(query, "<x/>");
        return (await act.Should().ThrowAsync<XQueryRuntimeException>("the query is {0}", query)).Which.ErrorCode;
    }

    // Serialization 3.1 §3: "It is a serialization error [err:SEPM0016] if a parameter value is
    // invalid for the given parameter."
    [Theory]
    [MemberData(nameof(NoMethod))]
    public async Task A_map_whose_method_names_no_method_is_SEPM0016(string method)
        => (await ErrorCodeOf($"serialize(<a>x</a>, map {{ 'method': '{method}' }})")).Should().Be("SEPM0016");

    // Serialization 3.1 §3.1: an output:serialization-parameters element that is not valid is SEPM0017.
    [Theory]
    [MemberData(nameof(NoMethod))]
    public async Task An_element_whose_method_names_no_method_is_SEPM0017(string method)
        => (await ErrorCodeOf($"serialize(<a>x</a>, {Element(method)})")).Should().Be("SEPM0017");

    // F&O 3.1 §14.7.3: in a map the methods of Serialization 3.1 are strings; an xs:QName "must have a
    // non-absent namespace URI" and names an implementation-defined method (this processor has none).
    // A QName was read as its local name, so both of the first two were the xml method.
    [Theory]
    [InlineData("QName('', 'xml')")]
    [InlineData("QName('http://example.com/m', 'xml')")]
    [InlineData("QName('http://example.com/m', 'm')")]
    public async Task A_map_whose_method_is_an_xs_QName_is_SEPM0016(string method)
        => (await ErrorCodeOf($"serialize(<a>x</a>, map {{ 'method': {method} }})")).Should().Be("SEPM0016");

    [Fact]
    public async Task A_prefixed_method_in_an_element_is_SEPM0017()
        => (await ErrorCodeOf($"serialize(<a>x</a>, <output:serialization-parameters xmlns:output='{OutputNs}' xmlns:p='http://example.com/m'><output:method value='p:m'/></output:serialization-parameters>)"))
            .Should().Be("SEPM0017");

    // Each name of §3, both forms; text is the one whose output differs from the element's markup.
    [Theory]
    [InlineData("xml")]
    [InlineData("xhtml")]
    [InlineData("html")]
    [InlineData("text")]
    [InlineData("json")]
    [InlineData("adaptive")]
    public async Task Each_method_of_Serialization_3_1_is_accepted_in_both_forms(string method)
    {
        var fromMap = await _facade.EvaluateAsync($"serialize(<a>x</a>, map {{ 'method': '{method}' }})", "<x/>");
        var fromElement = await _facade.EvaluateAsync($"serialize(<a>x</a>, {Element(method)})", "<x/>");

        fromElement.Should().Be(fromMap);
        if (method == "text")
            fromMap.Should().Be("x");
    }

    // Q{}text is the no-namespace name text; white space at both ends is not part of the name.
    [Theory]
    [InlineData("Q{}text")]
    [InlineData(" text ")]
    public async Task A_method_written_as_an_EQName_or_with_white_space_is_read(string method)
    {
        (await _facade.EvaluateAsync($"serialize(<a>x</a>, map {{ 'method': '{method}' }})", "<x/>")).Should().Be("x");
        (await _facade.EvaluateAsync($"serialize(<a>x</a>, {Element(method)})", "<x/>")).Should().Be("x");
    }

    // The prolog declares the same parameter (QT3 Serialization-017 allows SEPM0016).
    [Theory]
    [InlineData("XML")]
    [InlineData("INVALID_VALUE")]
    [InlineData("Q{http://example.com/m}m")]
    public void A_prolog_method_that_names_no_method_is_SEPM0016(string method)
    {
        var act = () => XQueryFacade.DetectSerializationOptions(Output + $"declare option output:method \"{method}\"; <a/>");

        act.Should().Throw<XQueryRuntimeException>().Which.ErrorCode.Should().Be("SEPM0016");
    }

    [Fact]
    public async Task A_query_whose_prolog_method_names_no_method_is_SEPM0016()
        => (await ErrorCodeOf(Output + "declare option output:method \"XML\"; <a/>")).Should().Be("SEPM0016");

    // The references of the prolog's string literal are expanded before the white space at both ends
    // is removed: " Q{}xml&#x9;" is xml (QT3 K2-Serialization-29). It was read as "xml&#x9;".
    [Theory]
    [InlineData(" Q{}xml&#x9;", OutputMethod.Xml)]
    [InlineData("&#x74;ext", OutputMethod.Text)]
    [InlineData("&#32;html&#10;", OutputMethod.Html)]
    public void A_prolog_method_with_references_is_read_with_them_expanded(string method, OutputMethod expected)
        => XQueryFacade.DetectSerializationOptions(Output + $"declare option output:method \"{method}\"; <a/>")
            .Method.Should().Be(expected);

    // Another option's references are expanded too: indent "&#x79;es" is yes (the default is no). QT3
    // Serialization-033 writes no as "&#x6e;&#x6f;".
    [Fact]
    public void A_prolog_yes_no_option_with_references_is_read_with_them_expanded()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:method \"xml\"; declare option output:indent \"&#x79;es\"; <a/>")
            .Indent.Should().BeTrue();

    // A malformed reference is left as written, for the parse to report: the prolog reader does not
    // throw the parser's exception, and a run stops at the parse (XPST0003).
    [Fact]
    public async Task A_malformed_reference_in_a_prolog_option_is_left_to_the_parse()
    {
        const string query = Output + "declare option output:item-separator \"&bogus;\"; <a/>";

        XQueryFacade.DetectSerializationOptions(query).ItemSeparator.Should().Be("&bogus;");
        var run = () => _facade.EvaluateAsync(query, "<x/>");
        (await run.Should().ThrowAsync<Exception>()).Which.Message.Should().StartWith("XPST0003");
    }

    // The reader the command line uses for the prolog's method: the facade's, with no other parameter.
    [Theory]
    [InlineData("declare option output:method \" Q{}xml&#x9;\"; <a/>", OutputMethod.Xml)]
    [InlineData("declare option output:method \"text\"; <a/>", OutputMethod.Text)]
    [InlineData("declare option output:indent \"yes\"; <a/>", null)]
    [InlineData("<a/>", null)]
    public void The_declared_method_is_read_as_the_facade_reads_it(string query, OutputMethod? expected)
        => XQueryFacade.DeclaredOutputMethod(Output + query).Should().Be(expected);

    [Fact]
    public void A_declared_method_that_names_no_method_is_SEPM0016()
    {
        var act = () => XQueryFacade.DeclaredOutputMethod(Output + "declare option output:method \"XML\"; <a/>");

        act.Should().Throw<XQueryRuntimeException>().Which.ErrorCode.Should().Be("SEPM0016");
    }

    // A parameter document is an output:serialization-parameters element (§3.1).
    [Fact]
    public void A_parameter_document_whose_method_names_no_method_is_SEPM0017()
    {
        File.WriteAllText(Path.Combine(_dir, "params.xml"), Element("XML"));

        var act = () => XQueryFacade.DetectSerializationOptions(
            Output + "declare option output:parameter-document \"params.xml\"; <a/>", new Uri(_dir + Path.DirectorySeparatorChar));

        act.Should().Throw<XQueryRuntimeException>().Which.ErrorCode.Should().Be("SEPM0017");
    }
}
