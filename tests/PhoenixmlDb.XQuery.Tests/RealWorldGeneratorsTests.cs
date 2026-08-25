using FluentAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// Gaps found by running Dimitre Novatchev's Generators library (Balisage 2026), a real
/// XPath 4.0 function library verified against BaseX and Saxon. Both were fatal — the code
/// would not lex, then would not name its own imports.
/// </summary>
public class RealWorldGeneratorsTests
{
    private readonly XQueryFacade _facade = new();
    private async Task<string> Eval(string xq) => await _facade.EvaluateAsync(xq);

    // ---- XPath 4.0 §4.2: '_' as a digit separator ----

    [Fact]
    public async Task Integer_literals_accept_digit_separators()
        => (await Eval("1_000_000 + 1")).Should().Be("1000001");

    /// <summary>
    /// The FRACTION and EXPONENT take separators too. Fixing only the integer part still lexed
    /// "1_0.5_0" as a decimal followed by a name '_0' — caught because the four numeric shapes
    /// were tested, not just the one the corpus happened to use.
    /// </summary>
    [Theory]
    [InlineData("1_0.5_0", "10.5")]
    [InlineData("1_0e1", "1.0E11")]
    public async Task Decimal_and_double_literals_accept_separators(string literal, string _)
        => (await Eval($"string({literal}) != ''")).Should().Be("true");

    /// <summary>
    /// The separator must sit BETWEEN digits. The grammar enforces that structurally
    /// ([0-9]+ ('_' [0-9]+)*), so stripping underscores afterwards cannot launder a malformed
    /// literal — "1__0" and "1_" are lexical errors, not numbers.
    /// </summary>
    [Theory]
    [InlineData("1__0")]
    [InlineData("1_")]
    public async Task Malformed_separators_are_rejected(string literal)
    {
        var act = async () => await Eval(literal);
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Plain_numerics_are_unaffected()
    {
        (await Eval("123 + 1")).Should().Be("124");
        (await Eval("4.5 * 2")).Should().Be("9");
    }

    // ---- import module binds its prefix ----

    /// <summary>
    /// `import module namespace p = "uri"` binds p in the statically known namespaces
    /// (XQuery 3.1 §4.11). The prefix was extracted into the AST and never registered, so every
    /// use of it raised XPST0081 — a module-using query could not name a single function it
    /// imported.
    ///
    /// Identical to the import-schema defect fixed a day earlier. The two sites sit in the same
    /// file, were written alike, and were broken alike; fixing one and not checking the other is
    /// what let this survive.
    /// </summary>
    [Fact]
    public async Task Import_module_binds_its_prefix()
    {
        // The module cannot be resolved, so this must fail on the MODULE, never on the prefix.
        var act = async () => await Eval("import module namespace m = \"urn:nope\"; m:f()");
        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.Message.Should().NotContain("XPST0081");
        ex.Which.Message.Should().NotContain("Unbound namespace prefix");
    }

    // ---- XPath 4.0 surface the corpus needed ----

    /// <summary>declare record NAME(...) — the named record type and its constructor.</summary>
    [Fact]
    public async Task Named_record_declaration_and_constructor()
        => (await Eval("""
            declare namespace rec = "urn:rec";
            declare record rec:point ( x as xs:integer, y as xs:integer );
            rec:point(x := 7, y := 2)?x
            """)).Should().Be("7");

    /// <summary>The declared name is usable as a type in a signature.</summary>
    [Fact]
    public async Task Record_name_is_usable_as_a_type()
        => (await Eval("""
            declare namespace rec = "urn:rec";
            declare record rec:point ( x as xs:integer, y as xs:integer );
            declare function rec:getx($p as rec:point) as xs:integer { $p?x };
            rec:getx(rec:point(x := 7, y := 2))
            """)).Should().Be("7");

    /// <summary>
    /// The mapping arrow is record METHOD DISPATCH: E =?> name(args) is E?name(E, args). Reading
    /// it as a per-item map over a global function — which the name suggests — reports
    /// "Unknown function" against a module that defines no such function.
    /// </summary>
    [Fact]
    public async Task Mapping_arrow_dispatches_to_a_record_field()
        => (await Eval("""
            declare namespace rec = "urn:rec";
            declare record rec:counter ( n as xs:integer, bump as fn(rec:counter) as xs:integer );
            let $c := rec:counter(n := 41, bump := fn($this) { $this?n + 1 })
            return $c =?> bump()
            """)).Should().Be("42");

    /// <summary>
    /// fn is a synonym for function in TYPES, not only in inline expressions — XPath 4.0 §4.6.6.
    /// It was accepted only for expressions, which is the half Martin Honnen's report showed.
    /// </summary>
    [Fact]
    public async Task Fn_is_accepted_as_a_type_keyword()
        => (await Eval("""
            declare function local:apply($f as fn(item()) as xs:integer) { $f(1) };
            local:apply(fn($x) { 42 })
            """)).Should().Be("42");

    /// <summary>
    /// Function types may NAME their parameters — fn($this as T) as U — which is how record
    /// field declarations write callback signatures. The name is documentation: function types
    /// match structurally, so only the sequence type participates.
    /// </summary>
    [Fact]
    public async Task Function_types_may_name_their_parameters()
        => (await Eval("""
            declare function local:apply($f as fn($x as item()) as xs:integer) { $f(1) };
            local:apply(fn($x) { 42 })
            """)).Should().Be("42");

    [Fact]
    public async Task Array_empty_predicate_and_constructor_coexist()
    {
        (await Eval("array:empty([])")).Should().Be("true");
        (await Eval("array:empty([1,2])")).Should().Be("false");
    }

    /// <summary>fn:while-do applies the action while the predicate holds.</summary>
    [Fact]
    public async Task While_do_iterates_until_the_predicate_fails()
        => (await Eval("while-do(1, fn($n) { $n lt 10 }, fn($n) { $n * 2 })")).Should().Be("16");

    [Fact]
    public async Task While_do_returns_input_untouched_when_predicate_is_false_initially()
        => (await Eval("while-do(99, fn($n) { $n lt 10 }, fn($n) { $n * 2 })")).Should().Be("99");
}
