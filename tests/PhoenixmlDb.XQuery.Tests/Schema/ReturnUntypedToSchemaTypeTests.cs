using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Schema;

/// <summary>
/// A function whose declared result is a schema type casts an xs:untypedAtomic result to it
/// through the schema: a union tries its members in order. It was cast to the item type the
/// union stands in for, which left it untyped, and the result then failed the declared type
/// (QT3 FunctionCall-037, -038).
/// </summary>
public class ReturnUntypedToSchemaTypeTests
{
    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:u">
          <xs:simpleType name="dateOrInt"><xs:union memberTypes="xs:date xs:integer"/></xs:simpleType>
          <xs:simpleType name="small"><xs:restriction base="xs:integer"><xs:maxInclusive value="9"/></xs:restriction></xs:simpleType>
        </xs:schema>
        """;

    private static async Task<string> Eval(string query)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString("urn:u", Xsd);
        var engine = new QueryEngine(nodeProvider: new XdmDocumentStore(), schemaProvider: schemas);
        var compiled = engine.Compile("import schema namespace u = 'urn:u'; " + query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("declare function local:f() as u:dateOrInt { xs:untypedAtomic('2012-12-12') };", "local:f() instance of xs:date", "true")]
    [InlineData("declare function local:f() as u:dateOrInt { xs:untypedAtomic('12') };", "local:f() instance of xs:integer", "true")]
    [InlineData("declare function local:f($y) as u:dateOrInt { xs:untypedAtomic($y) };", "local:f(?)('2012-12-12') instance of xs:date", "true")]
    [InlineData("declare function local:f() as u:small { xs:untypedAtomic('7') };", "local:f() + 1", "8")]
    public async Task An_untyped_result_is_cast_to_the_schema_type(string declaration, string expression, string expected)
        => (await Eval($"{declaration} string({expression})")).Should().Be(expected);

    [Fact]
    public async Task A_value_outside_the_type_is_a_cast_error()
    {
        var act = () => Eval("declare function local:f() as u:small { xs:untypedAtomic('70') }; local:f()");
        (await act.Should().ThrowAsync<Exception>()).Which.Message.Should().NotBeNullOrEmpty();
    }
}
