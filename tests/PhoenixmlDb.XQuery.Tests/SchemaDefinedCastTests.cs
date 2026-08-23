using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// cast/castable against a SCHEMA-DEFINED simple type — a type an imported schema declares,
/// rather than one of the built-in XSD types the ItemType enum knows.
///
/// Previously the parser rejected any type whose prefix was not bound to the XSD namespace,
/// and reported it as "XPST0081: Unbound namespace prefix" — which was false: the schema
/// loaded, the prefix bound, and &lt;s:e/&gt; and s:f() both worked in the same query. That one
/// wrong message made ~350 QT3 failures across six unrelated test-sets look like a namespace
/// problem.
///
/// Facet validation is the schema provider's, via XmlSchemaDatatype.ParseValue — which already
/// enforces pattern, enumeration, length, bounds and whitespace for every XSD simple type,
/// unions and lists included. The engine has no facet machinery and should not grow one.
/// </summary>
public class SchemaDefinedCastTests
{
    private const string Ns = "urn:test:types";

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   targetNamespace="urn:test:types"
                   elementFormDefault="qualified">
          <xs:simpleType name="shortString">
            <xs:restriction base="xs:string"><xs:maxLength value="3"/></xs:restriction>
          </xs:simpleType>
          <xs:simpleType name="smallInt">
            <xs:restriction base="xs:integer">
              <xs:minInclusive value="1"/><xs:maxInclusive value="10"/>
            </xs:restriction>
          </xs:simpleType>
          <xs:simpleType name="colour">
            <xs:restriction base="xs:string">
              <xs:enumeration value="red"/><xs:enumeration value="green"/>
            </xs:restriction>
          </xs:simpleType>
          <xs:simpleType name="intList"><xs:list itemType="xs:integer"/></xs:simpleType>
          <xs:simpleType name="intOrDate">
            <xs:union memberTypes="xs:integer xs:date"/>
          </xs:simpleType>
          <xs:complexType name="box"><xs:sequence/></xs:complexType>
        </xs:schema>
        """;

    private static async Task<string> Eval(string body)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString(Ns, Xsd);
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);

        var query = $"import schema namespace t = \"{Ns}\";\n{body}";
        var compiled = engine.Compile(query);
        if (!compiled.Success)
            throw new InvalidOperationException(string.Join("; ", compiled.Errors));

        var ctx = engine.CreateContext();
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(ctx))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    // --- castable: facets decide ---

    [Fact]
    public async Task Castable_respects_maxLength_facet()
    {
        (await Eval("'ab' castable as t:shortString")).Should().Be("True");
        (await Eval("'abcd' castable as t:shortString")).Should().Be("False");
    }

    [Fact]
    public async Task Castable_respects_numeric_bounds()
    {
        (await Eval("'5' castable as t:smallInt")).Should().Be("True");
        (await Eval("'50' castable as t:smallInt")).Should().Be("False");
    }

    [Fact]
    public async Task Castable_respects_enumeration()
    {
        (await Eval("'red' castable as t:colour")).Should().Be("True");
        (await Eval("'purple' castable as t:colour")).Should().Be("False");
    }

    // List and union types come free: ParseValue handles them, which is the whole reason for
    // delegating rather than reimplementing.
    [Fact]
    public async Task Castable_handles_list_types()
    {
        (await Eval("'1 2 3' castable as t:intList")).Should().Be("True");
        (await Eval("'1 x 3' castable as t:intList")).Should().Be("False");
    }

    [Fact]
    public async Task Castable_handles_union_types()
    {
        (await Eval("'42' castable as t:intOrDate")).Should().Be("True");
        (await Eval("'2026-08-23' castable as t:intOrDate")).Should().Be("True");
        (await Eval("'not-either' castable as t:intOrDate")).Should().Be("False");
    }

    // --- cast: same predicate, different failure mode ---

    [Fact]
    public async Task Cast_succeeds_for_a_valid_value()
        => (await Eval("'ab' cast as t:shortString")).Should().Be("ab");

    [Fact]
    public async Task Cast_raises_FORG0001_for_a_facet_violation()
    {
        // castable returns false for this input; cast must raise, not return something odd.
        var act = async () => await Eval("'abcd' cast as t:shortString");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FORG0001");
    }

    // --- errors ABOUT THE QUERY stay errors, and must not degrade to castable=false ---

    [Fact]
    public async Task Unknown_schema_type_is_a_static_error_not_a_false()
    {
        var act = async () => await Eval("'x' castable as t:noSuchType");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Complex_type_is_rejected_as_a_cast_target()
    {
        var act = async () => await Eval("'x' castable as t:box");
        await act.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// Only cast/castable accept a schema-defined type. Everywhere else an unrecognised type
    /// must still be an error — returning AnyAtomicType for `instance of` would silently match
    /// ANYTHING, turning a loud failure into wrong answers.
    /// </summary>
    [Fact]
    public async Task Instance_of_does_not_silently_accept_a_schema_type()
    {
        var act = async () => await Eval("'x' instance of t:shortString");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Builtin_types_are_unaffected()
    {
        (await Eval("'5' castable as xs:integer")).Should().Be("True");
        (await Eval("'x' castable as xs:integer")).Should().Be("False");
    }
}
