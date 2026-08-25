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
}
