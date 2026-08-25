using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// The engine described its own implementation to people who wrote XQuery.
/// <c>XdmSequenceType.ToString()</c> rendered the CLR enum member, so declaring
/// <c>$n as xs:nonNegativeInteger</c> and passing 2 reported "does not match parameterized
/// type Integer" — not parameterized, not xs:integer, and "Integer" erases exactly the
/// derived/base distinction that caused the mismatch.
///
/// That message cost a real investigation into behaviour that was correct, and the same root
/// cause reached a shipped function: <c>fn:type(xs:byte(1))</c> returned the CLR class name.
/// Half of this file therefore pins the messages, and half pins the CORRECT behaviour the
/// bad messages made look broken — so it is not "fixed" later.
/// </summary>
public class TypeNameDiagnosticsTests
{
    private readonly XQueryFacade _facade = new();
    private async Task<string> Eval(string xq) => await _facade.EvaluateAsync(xq);

    private async Task<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException> Failing(string xq)
    {
        var act = async () => await Eval(xq);
        var ex = await act.Should().ThrowAsync<PhoenixmlDb.XQuery.Execution.XQueryRuntimeException>();
        return ex.Which;
    }

    // ---- fn:type must report XQuery type names, never CLR ones ----

    /// <summary>
    /// Tagged subtypes matched no arm of fn:type's switch and fell to the default, which
    /// returned <c>item.GetType().Name</c> — so xs:byte(1) reported "XsTypedInteger".
    /// </summary>
    [Theory]
    [InlineData("xs:byte(1)", "xs:byte")]
    [InlineData("xs:long(1)", "xs:long")]
    [InlineData("xs:positiveInteger(1)", "xs:positiveInteger")]
    [InlineData("xs:token('a')", "xs:token")]
    [InlineData("xs:NCName('a')", "xs:NCName")]
    public async Task Fn_type_names_derived_subtypes(string ctor, string expected)
        => (await Eval($"fn:type({ctor})?name")).Should().Be(expected);

    /// <summary>A tagged integer is an atomic value, not an opaque "item".</summary>
    [Fact]
    public async Task Fn_type_reports_atomic_kind_for_derived_subtypes()
        => (await Eval("fn:type(xs:byte(1))?kind")).Should().Be("atomic");

    /// <summary>
    /// An UNTAGGED integer is xs:integer and nothing narrower, however its value happens to
    /// fall. Pinned because the fix must not start guessing subtypes from range.
    /// </summary>
    [Fact]
    public async Task Fn_type_does_not_infer_a_subtype_from_range()
        => (await Eval("fn:type(2)?name")).Should().Be("xs:integer");

    /// <summary>No CLR type name may reach fn:type's output for any ordinary value.</summary>
    [Theory]
    [InlineData("2"), InlineData("'a'"), InlineData("xs:byte(1)"), InlineData("[1,2]")]
    [InlineData("map{'a':1}"), InlineData("true()"), InlineData("xs:date('2026-01-01')")]
    public async Task Fn_type_never_leaks_a_clr_name(string expr)
    {
        var name = await Eval($"fn:type({expr})?name");
        name.Should().MatchRegex("^(xs:[A-Za-z]+|map\\(\\*\\)|array\\(\\*\\)|function\\(\\*\\)|item\\(\\)\\*?|"
            + "element\\(\\)|attribute\\(\\)|text\\(\\)|comment\\(\\)|document-node\\(\\)|"
            + "processing-instruction\\(\\)|empty-sequence\\(\\))$");
    }

    // ---- Type-mismatch messages must name the declared type, and what arrived ----

    /// <summary>
    /// The message that started it all. It must name xs:nonNegativeInteger — the type actually
    /// written — and must not call a plain atomic type "parameterized".
    /// </summary>
    [Fact]
    public async Task Parameter_mismatch_names_the_declared_type()
    {
        var ex = await Failing("declare function local:f($n as xs:nonNegativeInteger) { $n }; local:f(2)");
        ex.Message.Should().Contain("xs:nonNegativeInteger");
        ex.Message.Should().Contain("xs:integer");           // and what actually arrived
        ex.Message.Should().NotContain("parameterized");
        ex.Message.Should().NotContain("Int64");
    }

    /// <summary>A let mismatch says the declared type in source syntax, not "Double".</summary>
    [Fact]
    public async Task Let_mismatch_names_the_declared_type()
    {
        var ex = await Failing("let $x as xs:double := 'a' return $x");
        ex.Message.Should().Contain("xs:double");
        ex.Message.Should().Contain("xs:string");
    }

    // ---- Correct behaviour that the bad messages made look like bugs ----

    /// <summary>
    /// RETRACTED BUG (BUGS.md #13a). Function CONVERSION rules (XPath 3.1 §3.1.5.2) promote
    /// numeric→double/float, anyURI→string and cast untypedAtomic. They never NARROW a
    /// supertype to a subtype. The literal 2 is xs:integer, and xs:nonNegativeInteger is a
    /// SUBTYPE of it, so matching fails and XPTY0004 is correct — Saxon and BaseX agree.
    /// Write xs:nonNegativeInteger(2) to pass one.
    /// </summary>
    [Theory]
    [InlineData("xs:nonNegativeInteger"), InlineData("xs:positiveInteger")]
    [InlineData("xs:long"), InlineData("xs:short"), InlineData("xs:unsignedByte")]
    public async Task Plain_integer_does_not_satisfy_a_derived_integer_parameter(string type)
    {
        var ex = await Failing($"declare function local:f($n as {type}) {{ $n }}; local:f(2)");
        ex.ErrorCode.Should().Be("XPTY0004");
    }

    /// <summary>...and the properly-constructed value is accepted, so the check is not blanket.</summary>
    [Fact]
    public async Task A_correctly_typed_derived_integer_is_accepted()
        => (await Eval("declare function local:f($n as xs:nonNegativeInteger) { $n + 1 }; "
                     + "local:f(xs:nonNegativeInteger(2))")).Should().Be("3");

    /// <summary>
    /// RETRACTED BUG (BUGS.md #13b). A let binding MATCHES its declared type (XQuery 3.1
    /// §3.10.2); it does not convert. This is the pair that proves the engine draws the line
    /// where the specs draw it: identical value, identical target type, different rule.
    /// </summary>
    [Fact]
    public async Task Let_matches_its_declared_type_but_a_parameter_converts()
    {
        (await Failing("let $x as xs:double := 1 return $x")).ErrorCode.Should().Be("XPTY0004");
        // "1.0e0", not "1" — the xs:double rendering IS the evidence that conversion ran.
        (await Eval("declare function local:g($x as xs:double) { $x }; local:g(1)")).Should().Be("1.0e0");
    }
}
