using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// An atomic value made as a schema-defined type (by a cast, a constructor function, or the
/// atomization of a validated node) is an instance of that type and of the types it is derived
/// from. The engine held such a value as its built-in base type and nothing more, so
/// <c>t:size('8') instance of t:size</c> was false and a parameter <c>as t:size</c> refused it.
/// </summary>
public sealed class SchemaTypedAtomicValueTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-typed-atomic").FullName;
    private readonly string _prolog;

    public SchemaTypedAtomicValueTests()
    {
        var file = Path.Combine(_dir, "t.xsd");
        File.WriteAllText(file, """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:t" xmlns="urn:t" elementFormDefault="qualified">
              <xs:simpleType name="size"><xs:restriction base="xs:integer"><xs:maxInclusive value="9"/></xs:restriction></xs:simpleType>
              <xs:simpleType name="small"><xs:restriction base="size"><xs:maxInclusive value="3"/></xs:restriction></xs:simpleType>
              <xs:simpleType name="code"><xs:restriction base="xs:string"><xs:pattern value="[A-Z]*"/></xs:restriction></xs:simpleType>
              <xs:simpleType name="other"><xs:restriction base="xs:integer"/></xs:simpleType>
              <xs:simpleType name="sizes"><xs:list itemType="size"/></xs:simpleType>
              <xs:simpleType name="either"><xs:union memberTypes="size xs:boolean"/></xs:simpleType>
              <xs:element name="n" type="size"/>
              <xs:element name="list" type="sizes"/>
              <xs:element name="e"><xs:complexType><xs:attribute name="a" type="small"/></xs:complexType></xs:element>
            </xs:schema>
            """);
        _prolog = $"import schema namespace t = 'urn:t' at '{new Uri(file).AbsoluteUri}'; ";
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<string> Run(string query)
    {
        try
        {
            return (await new XQueryFacade().EvaluateAsync(_prolog + query)).Trim();
        }
#pragma warning disable CA1031 // any failure is the outcome under test
        catch (Exception e)
#pragma warning restore CA1031
        {
            return "error: " + e.Message;
        }
    }

    [Theory]
    // made by a constructor function and by a cast
    [InlineData("t:size('8') instance of t:size", "true")]
    [InlineData("('8' cast as t:size) instance of t:size", "true")]
    [InlineData("(8 cast as t:size) instance of t:size", "true")]
    [InlineData("t:code('ABC') instance of t:code", "true")]
    [InlineData("t:code('') instance of xs:string", "true")]
    // still an instance of what the type restricts
    [InlineData("t:size('8') instance of xs:integer", "true")]
    [InlineData("t:size('8') instance of xs:decimal", "true")]
    // a type derived from a schema type is an instance of both
    [InlineData("t:small('2') instance of t:size", "true")]
    [InlineData("t:small('2') instance of t:small", "true")]
    [InlineData("t:size('2') instance of t:small", "false")]
    // a value of the base type or of an unrelated type is not
    [InlineData("8 instance of t:size", "false")]
    [InlineData("t:other('8') instance of t:size", "false")]
    [InlineData("xs:integer(t:size('8')) instance of t:size", "false")]
    // what is computed from it is a new value of the built-in type
    [InlineData("(t:size('8') + 0) instance of t:size", "false")]
    [InlineData("t:size('8') + 1", "9")]
    [InlineData("t:size('8') = 8", "true")]
    // the type travels with the value
    [InlineData("let $s := t:size('8') return $s instance of t:size", "true")]
    [InlineData("(1, t:size('8'), 'x')[2] instance of t:size", "true")]
    [InlineData("every $s in (t:size('1'), t:small('2')) satisfies $s instance of t:size", "true")]
    [InlineData("(t:size('1'), 2) instance of t:size+", "false")]
    [InlineData("declare function local:f($x as t:size) as t:size { $x }; local:f(t:size('4'))", "4")]
    [InlineData("declare function local:f($x as t:size) { $x }; local:f(t:small('2'))", "2")]
    [InlineData("t:size('4') treat as t:size", "4")]
    [InlineData("typeswitch (t:small('2')) case t:small return 'small' case t:size return 'size' default return 'no'", "small")]
    [InlineData("typeswitch (t:size('5')) case t:small return 'small' case t:size return 'size' default return 'no'", "size")]
    // a union: the member that matched
    [InlineData("t:either('7') instance of t:size", "true")]
    [InlineData("t:size('7') instance of t:either", "true")]
    // the typed value of a validated node
    [InlineData("data(validate { <t:n>7</t:n> }) instance of t:size", "true")]
    [InlineData("data(validate { <t:n>7</t:n> }) instance of xs:integer", "true")]
    [InlineData("data(validate { <t:e a='2'/> }/@a) instance of t:small", "true")]
    [InlineData("data(validate { <t:e a='2'/> }/@a) instance of t:size", "true")]
    [InlineData("every $i in data(validate { <t:list>1 2 3</t:list> }) satisfies $i instance of t:size", "true")]
    [InlineData("data(<t:n>7</t:n>) instance of t:size", "false")]
    public async Task A_value_made_as_a_schema_type_is_an_instance_of_it(string query, string expected)
        => (await Run(query)).Should().Be(expected);

    [Theory]
    [InlineData("declare function local:f($x as t:size) { $x }; local:f(8)")]
    [InlineData("declare function local:f($x as t:small) { $x }; local:f(t:size('2'))")]
    [InlineData("8 treat as t:size")]
    public async Task A_value_of_another_type_is_refused_where_the_schema_type_is_required(string query)
        => (await Run(query)).Should().StartWith("error:");
}
