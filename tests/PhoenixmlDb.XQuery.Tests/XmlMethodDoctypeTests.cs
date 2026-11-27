using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Security;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Serialization 3.1 §5.1.7: "If the doctype-system parameter is specified, the XML output method MUST
/// output a document type declaration immediately before the first element. The name following
/// &lt;!DOCTYPE MUST be the name of the first element, if any. If the doctype-public parameter is also
/// specified, then the XML output method MUST output PUBLIC followed by the public identifier and then
/// the system identifier; otherwise, it MUST output SYSTEM followed by the system identifier. The
/// internal subset MUST be empty. The doctype-public parameter MUST be ignored unless the doctype-system
/// parameter is specified." The xml method wrote no declaration at all (3.2.0 too); the xhtml and html
/// methods and the Saxon layout (phx:indent-style) wrote one. fn:serialize (map and element), the
/// prolog options and fn:put share the serializer.
/// </summary>
public sealed class XmlMethodDoctypeTests : IDisposable
{
    private const string Output = "http://www.w3.org/2010/xslt-xquery-serialization";
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-doctype").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static Task<string> Eval(string query) => new XQueryFacade().EvaluateAsync(query, "<x/>");

    private const string Sys = "map { 'doctype-system': 'a.dtd' }";

    public static TheoryData<string, string> Written => new()
    {
        { $"serialize(<n/>, {Sys})", "<!DOCTYPE n SYSTEM \"a.dtd\"><n/>" },
        { $"serialize(document {{ <n/> }}, {Sys})", "<!DOCTYPE n SYSTEM \"a.dtd\"><n/>" },
        { $"let $f := serialize#2 return $f(<n/>, {Sys})", "<!DOCTYPE n SYSTEM \"a.dtd\"><n/>" },
        { "serialize(document { <n/> }, map { 'doctype-system': 'a.dtd', 'omit-xml-declaration': false() })",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE n SYSTEM \"a.dtd\"><n/>" },
        { "serialize(document { <n/> }, map { 'doctype-system': 'a.dtd', 'omit-xml-declaration': false(), 'standalone': true() })",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><!DOCTYPE n SYSTEM \"a.dtd\"><n/>" },
        { $"serialize(<n/>, <output:serialization-parameters xmlns:output='{Output}'><output:doctype-system value='a.dtd'/></output:serialization-parameters>)",
            "<!DOCTYPE n SYSTEM \"a.dtd\"><n/>" },
        { "serialize(<n/>, map { 'doctype-system': 'a.dtd', 'doctype-public': '-//X//Y' })", "<!DOCTYPE n PUBLIC \"-//X//Y\" \"a.dtd\"><n/>" },
        { "serialize(<p:n xmlns:p='urn:p'/>, map { 'doctype-system': 'a.dtd' })", "<!DOCTYPE p:n SYSTEM \"a.dtd\"><p:n xmlns:p=\"urn:p\"/>" },
        { "serialize(document { comment { 'c' }, <n/> }, map { 'doctype-system': 'a.dtd' })", "<!--c--><!DOCTYPE n SYSTEM \"a.dtd\"><n/>" },
        { "serialize(<n><m/><m/></n>, map { 'doctype-system': 'a.dtd' })", "<!DOCTYPE n SYSTEM \"a.dtd\"><n><m/><m/></n>" },
        { "serialize(<n/>, map { 'doctype-system': 'a\"b.dtd' })", "<!DOCTYPE n SYSTEM 'a\"b.dtd'><n/>" },
        { "serialize(<n><m/></n>, map { 'doctype-system': 'a.dtd', 'indent': true() })", "<!DOCTYPE n SYSTEM \"a.dtd\">\n<n>\n  <m/>\n</n>" },
        { "serialize(document { comment { 'c' }, <n><m/></n> }, map { 'doctype-system': 'a.dtd', 'indent': true() })",
            "<!--c-->\n<!DOCTYPE n SYSTEM \"a.dtd\">\n<n>\n  <m/>\n</n>" },
        { "serialize(document { <n><m/></n> }, map { 'doctype-system': 'a.dtd', 'indent': true(), 'omit-xml-declaration': false() })",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE n SYSTEM \"a.dtd\">\n<n>\n  <m/>\n</n>" },
    };

    [Theory]
    [MemberData(nameof(Written))]
    public async Task The_xml_method_writes_the_declaration_before_the_first_element(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    public static TheoryData<string, string> NotWritten => new()
    {
        // doctype-public alone is ignored.
        { "serialize(<n/>, map { 'doctype-public': '-//X//Y' })", "<n/>" },
        // A document that is not one element: §5 lets the serializer recover by not writing it.
        { "serialize(document { <a/>, <b/> }, map { 'doctype-system': 'a.dtd' })", "<a/><b/>" },
        { "serialize(document { 'x' }, map { 'doctype-system': 'a.dtd' })", "x" },
        // Another method: as before.
        { "serialize(<n/>, map { 'method': 'text', 'doctype-system': 'a.dtd' })", "" },
        { "serialize(<html xmlns='http://www.w3.org/1999/xhtml'/>, map { 'method': 'xhtml', 'doctype-system': 'a.dtd' })",
            "<!DOCTYPE html SYSTEM \"a.dtd\"><html xmlns=\"http://www.w3.org/1999/xhtml\"></html>" },
        { "serialize(<html/>, map { 'method': 'html', 'doctype-system': 'a.dtd', 'doctype-public': '-//W3C//DTD HTML 4.01//EN' })",
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01//EN\" \"a.dtd\"><html></html>" },
    };

    [Theory]
    [MemberData(nameof(NotWritten))]
    public async Task Without_a_system_identifier_or_a_single_element_nothing_changes(string query, string expected)
        => (await Eval(query)).Should().Be(expected);

    [Theory]
    [InlineData("serialize(<n/>, map { 'doctype-system': 'a\"b''c' })")]
    [InlineData("serialize(<n/>, map { 'doctype-system': 'a.dtd', 'doctype-public': 'caf&#xE9;' })")]
    public async Task An_identifier_that_cannot_be_written_is_SEPM0016(string query)
    {
        var act = () => Eval(query);

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("SEPM0016");
    }

    [Fact]
    public async Task The_prolog_option_writes_it_in_the_query_result()
        => (await Eval($"declare namespace output = '{Output}'; declare option output:method 'xml'; "
                + "declare option output:doctype-system 'a.dtd'; declare option output:omit-xml-declaration 'yes'; <n/>"))
            .Should().Be("<!DOCTYPE n SYSTEM \"a.dtd\"><n/>");

    [Fact]
    public async Task Fn_put_writes_it_in_the_file()
    {
        var file = Path.Combine(_dir, "n.xml");
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store)
        {
            ResourcePolicy = ResourcePolicy.CreateBuilder().AllowScheme("*").AllowWriteTo("file", null, _dir).Build(),
        };
        var compiled = engine.Compile($"put(<n/>, '{new Uri(file).AbsoluteUri}', "
            + $"<output:serialization-parameters xmlns:output='{Output}'><output:doctype-system value='a.dtd'/>"
            + "<output:omit-xml-declaration value='yes'/></output:serialization-parameters>)");
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));

        await foreach (var _ in engine.ExecuteAsync(compiled.ExecutionPlan!)) { }

        (await File.ReadAllTextAsync(file)).Should().Be("<!DOCTYPE n SYSTEM \"a.dtd\"><n/>");
    }
}
