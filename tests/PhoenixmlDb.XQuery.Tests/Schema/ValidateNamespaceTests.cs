using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// The analyzer's rewriting passes never descended into validate { } (nor a unary lookup's
/// key), so a prefix the prolog bound was left unresolved there: &lt;p:e/&gt; was built with its
/// prefix but no namespace, and the XSD reader failed with "'p' is an undeclared prefix" —
/// 31 QT3 validate/nilled/deep-equal cases.
/// </summary>
public class ValidateNamespaceTests
{
    private const string Ns = "urn:test:validate-ns";

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   xmlns:p="urn:test:validate-ns" targetNamespace="urn:test:validate-ns" elementFormDefault="qualified">
          <xs:element name="root" type="p:rootType"/>
          <xs:complexType name="rootType"><xs:sequence><xs:element name="e" minOccurs="0"/></xs:sequence></xs:complexType>
        </xs:schema>
        """;

    private static async Task<string> Eval(string query)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString(Ns, Xsd);
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);
        var compiled = engine.Compile(query);
        if (!compiled.Success)
            throw new InvalidOperationException(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData($"import schema namespace p = '{Ns}'; namespace-uri(validate {{ <p:root><p:e/></p:root> }})")]
    [InlineData($"declare namespace p = '{Ns}'; import schema '{Ns}'; namespace-uri(validate strict {{ <p:root/> }})")]
    [InlineData($"import schema '{Ns}'; namespace-uri(validate {{ element {{ QName('{Ns}', 'q:root') }} {{ element {{ QName('{Ns}', 'q:e') }} {{}} }} }})")]
    public async Task A_prefixed_operand_validates(string query) =>
        (await Eval(query)).Should().Be(Ns);

    /// <summary>
    /// The result of validating an element is an element (XQuery 3.1 §3.21). The annotating
    /// parse builds a document around it, and that document was returned in its place.
    /// </summary>
    [Theory]
    [InlineData($"import schema namespace p = '{Ns}'; let $v := validate {{ <p:root><p:e/></p:root> }} return string-join((string($v instance of document-node()), string($v instance of element()), local-name($v/*), string(count($v/p:e)), string(count($v/*/p:e)), string(count($v/..))), ';')")]
    public async Task Validating_an_element_yields_an_element(string query) =>
        (await Eval(query)).Should().Be("false;true;e;1;0;0");

    [Fact]
    public async Task A_lookup_key_is_resolved_like_any_expression() =>
        (await Eval($"declare namespace p = '{Ns}'; map {{ '{Ns}': 'found' }} ! ?(namespace-uri(<p:x/>))"))
            .Should().Be("found");

    /// <summary>
    /// Lax validation skips an undeclared element but not one it assesses: xsi:type makes the
    /// element's type known, and a content-model violation under it is XQDY0027 (QT3
    /// cbcl-validateexpr-1). So is an xsi:type whose prefix is not in scope (validateexpr-34):
    /// xs is statically known, but not an in-scope namespace of the constructed element.
    /// </summary>
    [Theory]
    [InlineData($"import schema namespace p = '{Ns}'; validate lax {{ <p:unknown xsi:type='p:rootType'><p:zz/></p:unknown> }}")]
    [InlineData("validate lax { <a xsi:type='xs:integer'>42</a> }")]
    public async Task Lax_validation_reports_errors_in_what_it_assesses(string query)
    {
        var act = () => Eval(query);
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().Contain("Validation failed");
    }

    [Fact]
    public async Task Lax_validation_skips_an_undeclared_element() =>
        (await Eval($"import schema namespace p = '{Ns}'; local-name(validate lax {{ <p:undeclared>1</p:undeclared> }})"))
            .Should().Be("undeclared");
}
