using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// The new name of a rename expression (XQUF 3.0 §5.4) is evaluated as though it were the name
/// expression of a computed element, attribute or processing-instruction constructor, by the kind of
/// the target, with those constructors' errors. Only its first item was taken, a string without a
/// colon became a local name unchecked, and an element's new name was in no namespace whatever the
/// default element namespace: a sequence, a comment, "" and "12" were accepted, and the renamed
/// document then could not be serialized (XQUTS id-rename-expr-020, -024, -026).
/// </summary>
public class RenameNewNameTests
{
    private const string D = "doc('urn:test:d')";
    private const string Source = "<r id='1'><a>a</a><b>b</b><!--c--><?p v?></r>";

    private static (XdmDocumentStore Store, QueryEngine Engine) Setup()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString(Source, "urn:test:d");
        return (store, new QueryEngine(nodeProvider: store, documentResolver: store));
    }

    private static async Task<string> RunAsync(XdmDocumentStore store, QueryEngine engine, string query)
    {
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors.Select(e => e.Message)));
        var items = new List<string>();
        await foreach (var item in engine.ExecuteAsync(compiled.ExecutionPlan!))
            items.Add(XQueryResultSerializer.Serialize(item, store));
        return string.Join(" ", items);
    }

    private static Task<string> DocumentAsync(XdmDocumentStore store, QueryEngine engine)
        => RunAsync(store, engine, $"{D}/r");

    // The target and the name: an element, an attribute or a processing instruction of the
    // document, renamed in place; the same in a copy.
    [Theory]
    [InlineData("r/a", "<!-- A comment node -->", "XQDY0074")]
    [InlineData("r/a", "('', 'element', '')", "XPTY0004")]
    [InlineData("r/a", "('x', 'y')", "XPTY0004")]
    [InlineData("r/a", "()", "XPTY0004")]
    [InlineData("r/a", "12", "XPTY0004")]
    [InlineData("r/a", "''", "XQDY0074")]
    [InlineData("r/a", "'12'", "XQDY0074")]
    [InlineData("r/a", "'a b'", "XQDY0074")]
    [InlineData("r/a", "'undeclared:x'", "XQDY0074")]
    [InlineData("r/a", "'xmlns:x'", "XQDY0074")]
    [InlineData("r/@id", "'12'", "XQDY0074")]
    [InlineData("r/@id", "''", "XQDY0074")]
    [InlineData("r/@id", "('x', 'y')", "XPTY0004")]
    [InlineData("r/@id", "'xmlns'", "XQDY0044")]
    [InlineData("r/processing-instruction()", "'a:b'", "XQDY0041")]
    [InlineData("r/processing-instruction()", "'12'", "XQDY0041")]
    [InlineData("r/processing-instruction()", "'xml'", "XQDY0064")]
    [InlineData("r/processing-instruction()", "xs:QName('q')", "XPTY0004")]
    [InlineData("r/processing-instruction()", "('q', 'r')", "XPTY0004")]
    public async Task A_new_name_its_constructor_would_refuse_is_that_error_and_nothing_changes(string target, string name, string code)
    {
        var (store, engine) = Setup();

        var inPlace = () => RunAsync(store, engine, $"rename node {D}/{target} as {name}");
        (await inPlace.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be(code);
        (await DocumentAsync(store, engine)).Should().Be(Source.Replace('\'', '"'));

        var inCopy = () => RunAsync(store, engine, $"copy $c := {D} modify rename node $c/{target} as {name} return $c");
        (await inCopy.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be(code);
    }

    [Theory]
    // Whitespace around a name is not part of it, as in the constructors.
    [InlineData("r/a", "' t '", "<r id=\"1\"><t>a</t><b>b</b><!--c--><?p v?></r>")]
    [InlineData("r/a", "xs:untypedAtomic('t')", "<r id=\"1\"><t>a</t><b>b</b><!--c--><?p v?></r>")]
    [InlineData("r/a", "<n>t</n>", "<r id=\"1\"><t>a</t><b>b</b><!--c--><?p v?></r>")]
    [InlineData("r/a", "QName('urn:q', 'q:t')", "<r id=\"1\"><q:t xmlns:q=\"urn:q\">a</q:t><b>b</b><!--c--><?p v?></r>")]
    [InlineData("r/@id", "' n '", "<r n=\"1\"><a>a</a><b>b</b><!--c--><?p v?></r>")]
    [InlineData("r/processing-instruction()", "' q '", "<r id=\"1\"><a>a</a><b>b</b><!--c--><?q v?></r>")]
    public async Task A_valid_new_name_renames_the_node(string target, string name, string document)
    {
        var (store, engine) = Setup();

        (await RunAsync(store, engine, $"rename node {D}/{target} as {name}")).Should().BeEmpty();
        (await DocumentAsync(store, engine)).Should().Be(document);

        var (store2, engine2) = Setup();
        (await RunAsync(store2, engine2, $"copy $c := {D} modify rename node $c/{target} as {name} return $c/r"))
            .Should().Be(document);
    }

    // An element's new name takes the default element namespace, as a computed element
    // constructor's does; an attribute's does not.
    [Fact]
    public async Task An_element_renamed_to_a_name_with_no_prefix_is_in_the_default_element_namespace()
    {
        var (store, engine) = Setup();
        const string Prolog = "declare default element namespace 'urn:d'; ";

        (await RunAsync(store, engine, Prolog + $"copy $c := <r><s/></r> modify rename node $c/s as 't' return namespace-uri($c/t)"))
            .Should().Be("urn:d");
        (await RunAsync(store, engine, Prolog + $"copy $c := <r x='1'/> modify rename node $c/@x as 'y' return namespace-uri($c/@y)"))
            .Should().Be("");
        (await RunAsync(store, engine, Prolog + $"copy $c := <r><s/></r> modify rename node $c/s as 't' return $c"))
            .Should().Be("<r xmlns=\"urn:d\"><t/></r>");
        (await RunAsync(store, engine, $"rename node {D}/Q{{}}r/Q{{}}a as 't'")).Should().BeEmpty();
        (await RunAsync(store, engine, $"namespace-uri({D}/r/t)")).Should().Be("");
    }

    // The namespaces of a copied element are read as fn:namespace-uri-for-prefix reads them: a copy's
    // default namespace was read as none, so a rename into it was refused (XUDY0023). A binding that
    // does conflict is still refused.
    [Fact]
    public async Task The_namespaces_of_a_copied_element_are_its_own()
    {
        var (store, engine) = Setup();

        (await RunAsync(store, engine,
            "copy $c := <r xmlns='urn:d'><s/></r> modify rename node $c/*:s as QName('urn:d', 't') return $c"))
            .Should().Be("<r xmlns=\"urn:d\"><t/></r>");
        (await RunAsync(store, engine,
            "declare namespace p = 'urn:p'; copy $c := <r xmlns:p='urn:p'><s/></r> modify insert node attribute p:x { 1 } into $c/s return $c"))
            .Should().Be("<r xmlns:p=\"urn:p\"><s p:x=\"1\"/></r>");

        var conflict = () => RunAsync(store, engine,
            "copy $c := <r xmlns='urn:d'><s/></r> modify rename node $c/*:s as QName('urn:other', 't') return $c");
        (await conflict.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0023");
        var attributeConflict = () => RunAsync(store, engine,
            "copy $c := <r xmlns:p='urn:p'><s/></r> modify insert node attribute { QName('urn:q', 'p:x') } { 1 } into $c/s return $c");
        (await attributeConflict.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0023");
    }
}
