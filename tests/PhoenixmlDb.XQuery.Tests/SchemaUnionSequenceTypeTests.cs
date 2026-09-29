using FluentAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// A union type declared by an imported schema, used as a SequenceType item type rather than
/// as a cast target: <c>instance of</c>, <c>typeswitch</c> and function signatures. A value
/// is an instance of a union when it is an instance of one of its member types (XQuery 3.1
/// §2.5.5.2), which needs no schema annotation on the value.
/// </summary>
public class SchemaUnionSequenceTypeTests
{
    private const string Ns = "urn:test:unions";

    private const string Xsd = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                   xmlns:u="urn:test:unions"
                   targetNamespace="urn:test:unions">
          <xs:simpleType name="intOrFloat">
            <xs:union memberTypes="xs:integer xs:float"/>
          </xs:simpleType>
          <xs:simpleType name="dateOrTime">
            <xs:union memberTypes="xs:date xs:time"/>
          </xs:simpleType>
          <xs:simpleType name="unionOfUnions">
            <xs:union memberTypes="u:intOrFloat u:dateOrTime"/>
          </xs:simpleType>
          <xs:simpleType name="restrictedUnion">
            <xs:restriction base="u:intOrFloat"><xs:pattern value="[0-9]+"/></xs:restriction>
          </xs:simpleType>
          <xs:simpleType name="intList"><xs:list itemType="xs:integer"/></xs:simpleType>
          <xs:simpleType name="listOrDate">
            <xs:union memberTypes="u:intList xs:date"/>
          </xs:simpleType>
          <xs:simpleType name="smallInt">
            <xs:restriction base="xs:integer"><xs:maxInclusive value="10"/></xs:restriction>
          </xs:simpleType>
        </xs:schema>
        """;

    private static async Task<string> Eval(string body)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString(Ns, Xsd);
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store, schemaProvider: schemas);

        var compiled = engine.Compile($"import schema namespace u = \"{Ns}\";\n{body}");
        if (!compiled.Success)
            throw new InvalidOperationException(string.Join("; ", compiled.Errors));

        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    private static IReadOnlyList<string> CompileErrorCodes(string body)
    {
        var schemas = new XsdSchemaProvider();
        schemas.AddFromString(Ns, Xsd);
        var engine = new QueryEngine(schemaProvider: schemas);
        var compiled = engine.Compile($"import schema namespace u = \"{Ns}\";\n{body}");
        return compiled.Errors.Select(e => e.Code).ToList();
    }

    [Theory]
    [InlineData("23 instance of u:intOrFloat", "True")]
    [InlineData("xs:float(1.5) instance of u:intOrFloat", "True")]
    [InlineData("xs:byte(3) instance of u:intOrFloat", "True")]
    [InlineData("'23' instance of u:intOrFloat", "False")]
    [InlineData("1.5 instance of u:intOrFloat", "False")]
    [InlineData("1.5e0 instance of u:intOrFloat", "False")]
    [InlineData("(1, xs:float(2)) instance of u:intOrFloat+", "True")]
    [InlineData("(1, 'x') instance of u:intOrFloat*", "False")]
    public async Task Instance_of_a_union_is_membership_in_any_member(string query, string expected) =>
        (await Eval(query)).Should().Be(expected);

    [Theory]
    [InlineData("23 instance of u:unionOfUnions", "True")]
    [InlineData("xs:date('2020-01-01') instance of u:unionOfUnions", "True")]
    [InlineData("'x' instance of u:unionOfUnions", "False")]
    public async Task A_union_of_unions_takes_the_members_of_its_members(string query, string expected) =>
        (await Eval(query)).Should().Be(expected);

    /// <summary>
    /// Only a pure union is a generalized atomic type. A union derived by restriction, or one
    /// with a list member, is XPST0051 as an item type (QT3 instanceof113, instanceof120).
    /// </summary>
    [Theory]
    [InlineData("23 instance of u:restrictedUnion")]
    [InlineData("23 instance of u:listOrDate")]
    [InlineData("typeswitch (23) case u:restrictedUnion return 1 default return 0")]
    [InlineData("function($in as u:restrictedUnion) { $in }(23)")]
    [InlineData("function() as u:restrictedUnion { 23 }()")]
    [InlineData("let $x as u:intList := 1 return $x")]
    [InlineData("23 instance of u:noSuchType")]
    public void A_type_that_is_not_generalized_atomic_is_a_static_error(string query) =>
        CompileErrorCodes(query).Should().Contain("XPST0051");

    /// <summary>
    /// The static check must fire even when the query would fail later for another reason
    /// (QT3 instanceof117: an unknown constructor alongside a restricted union).
    /// </summary>
    [Fact]
    public void The_item_type_check_is_static() =>
        CompileErrorCodes("u:noSuchFunction(1) instance of u:restrictedUnion").Should().Contain("XPST0051");

    [Fact]
    public async Task A_restricted_union_is_still_a_cast_target() =>
        (await Eval("('23' cast as u:restrictedUnion) instance of xs:integer")).Should().Be("True");

    [Fact]
    public async Task A_restricted_atomic_type_holds_no_unannotated_value() =>
        (await Eval("5 instance of u:smallInt")).Should().Be("False");

    [Theory]
    [InlineData("('23' cast as u:intOrFloat) instance of xs:integer", "True")]
    [InlineData("('1.5' cast as u:intOrFloat) instance of xs:float", "True")]
    [InlineData("('23' cast as u:intOrFloat) instance of u:intOrFloat", "True")]
    [InlineData("('2020-01-01' cast as u:unionOfUnions) instance of xs:date", "True")]
    [InlineData("(xs:float(2) cast as u:intOrFloat) instance of xs:float", "True")]
    public async Task Cast_to_a_union_yields_the_first_accepting_member(string query, string expected) =>
        (await Eval(query)).Should().Be(expected);

    [Fact]
    public async Task Cast_to_a_union_no_member_accepts_is_FORG0001()
    {
        var act = () => Eval("'abc' cast as u:intOrFloat");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FORG0001");
    }

    [Fact]
    public async Task Let_type_declaration_checks_union_membership()
    {
        (await Eval("let $x as u:intOrFloat := 3 return $x")).Should().Be("3");
        var act = () => Eval("let $x as u:intOrFloat := 'x' return $x");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }

    [Fact]
    public async Task Untyped_argument_is_cast_through_the_union_members() =>
        (await Eval("function($in as u:intOrFloat) { $in instance of xs:integer }(xs:untypedAtomic('7'))"))
            .Should().Be("True");

    /// <summary>
    /// Every simple type an imported schema declares has a constructor function of its own
    /// name, equivalent to <c>cast as T?</c> (XQuery 3.1 §3.18.4).
    /// </summary>
    [Theory]
    [InlineData("u:intOrFloat('23') instance of xs:integer", "True")]
    [InlineData("u:intOrFloat('1.5') instance of xs:float", "True")]
    [InlineData("u:smallInt('7') instance of xs:integer", "True")]
    [InlineData("u:smallInt('7') + 1", "8")]
    [InlineData("empty(u:intOrFloat(()))", "True")]
    [InlineData("u:intOrFloat#1('5') instance of xs:integer", "True")]
    [InlineData("function-lookup(QName('urn:test:unions', 'intOrFloat'), 1)('5') instance of xs:integer", "True")]
    public async Task A_schema_simple_type_has_a_constructor_function(string query, string expected) =>
        (await Eval(query)).Should().Be(expected);

    [Fact]
    public async Task A_constructor_enforces_the_facets()
    {
        var act = () => Eval("u:smallInt('70')");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("FORG0001");
    }

    [Fact]
    public async Task A_schema_import_prefix_is_in_scope_at_run_time() =>
        (await Eval("namespace-uri-from-QName(xs:QName('u:x'))")).Should().Be(Ns);

    [Fact]
    public async Task Typeswitch_selects_the_union_case_only_for_members() =>
        (await Eval("for $v in (1, 'x') return typeswitch ($v) case u:intOrFloat return 'num' default return 'other'"))
            .Should().Be("num,other");

    [Fact]
    public async Task Declared_function_parameter_accepts_members_and_rejects_others()
    {
        const string decl = "declare function local:f($in as u:intOrFloat) { $in + 1 };\n";
        (await Eval(decl + "local:f(23)")).Should().Be("24");
        var act = () => Eval(decl + "local:f('23')");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }

    [Fact]
    public async Task Inline_function_parameter_rejects_a_non_member()
    {
        var act = () => Eval("function($in as u:intOrFloat) { $in }('23')");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }

    [Fact]
    public async Task Declared_return_type_rejects_a_non_member()
    {
        var act = () => Eval("declare function local:f() as u:intOrFloat { 'x' };\nlocal:f()");
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPTY0004");
    }
}
