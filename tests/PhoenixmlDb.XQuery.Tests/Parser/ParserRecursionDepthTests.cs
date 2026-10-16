using AwesomeAssertions;
using PhoenixmlDb.XQuery.Parser;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Parser;

/// <summary>
/// Adversarial-audit finding I-antlr: the ANTLR recursive-descent parser (and the visitor that walks
/// its tree) recurse once per grammar rule, so a pathologically deep query — thousands of nested
/// parentheses or predicates — used to exhaust the native call stack and abort the host with an
/// uncatchable <see cref="System.StackOverflowException"/> (exit code 134) on untrusted input. The
/// parser now bounds nesting depth and fails with a catchable <see cref="XQueryParseException"/>.
///
/// If this guard regresses, these tests do not merely fail — the StackOverflow aborts the entire test
/// host with SIGABRT, which is itself the signal the fix is gone.
/// </summary>
[Trait("Category", "Parser")]
public class ParserRecursionDepthTests
{
    private readonly XQueryParserFacade _parser = new();

    [Theory]
    [InlineData(5_000)]
    [InlineData(50_000)]
    public void Parse_PathologicallyDeepParens_RaisesCatchableError(int depth)
    {
        // Empirically the unguarded parser/visitor overflow the native 1 MB stack at roughly 900
        // levels of nested parentheses; these depths are far past that. Must NOT crash the host.
        var query = new string('(', depth) + "1" + new string(')', depth);

        var ex = Record.Exception(() => _parser.Parse(query));

        ex.Should().BeOfType<XQueryParseException>(
            "deeply nested input must fail with a catchable parse error, never a StackOverflow");
        ex!.Message.Should().Contain("nesting depth");
    }

    [Fact]
    public void Parse_PathologicallyDeepPredicates_RaisesCatchableError()
    {
        // A different recursion shape (nested predicates) exercises the same guard.
        var depth = 20_000;
        var query = "$x" + string.Concat(System.Linq.Enumerable.Repeat("[.", depth)) +
                    string.Concat(System.Linq.Enumerable.Repeat("]", depth));

        var ex = Record.Exception(() => _parser.Parse(query));

        ex.Should().BeOfType<XQueryParseException>();
    }

    [Fact]
    public void TryParse_PathologicallyDeep_ReturnsNullWithErrors_NotCrash()
    {
        var query = new string('(', 30_000) + "1" + new string(')', 30_000);

        var ast = _parser.TryParse(query, out var errors);

        ast.Should().BeNull();
        errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("((((((((((1))))))))))")]                 // 10 nested parens — trivially legal
    [InlineData("for $x in (1,2,3) return $x * 2")]        // ordinary query
    public void Parse_NormalNesting_StillParses(string query)
    {
        var result = _parser.Parse(query);
        result.Should().NotBeNull();
    }

    [Fact]
    public void Parse_DeepButLegalNesting_JustUnderCap_StillParses()
    {
        // ~80 levels of parenthesis nesting (~1150 grammar-rule frames) sits below the MaxParseDepth
        // cap, proving the guard does not reject legitimately deep queries.
        const int depth = 80;
        var query = new string('(', depth) + "1 + 2" + new string(')', depth);

        var result = _parser.Parse(query);
        result.Should().NotBeNull();
    }

    /// <summary>
    /// The guard's error is reached on a 1&#160;MB thread, the default stack on Windows: with
    /// fewer, larger frames per nesting level (#401), a cap of 2500 frames overflowed there
    /// (Windows CI, net8.0) and on a 1&#160;MB thread on Linux, while the stack was unwinding.
    /// </summary>
    [Theory]
    [InlineData("parens")]
    [InlineData("predicates")]
    public void The_depth_error_is_reached_on_a_one_megabyte_stack(string shape)
    {
        var query = shape == "parens"
            ? new string('(', 5_000) + "1" + new string(')', 5_000)
            : "$x" + string.Concat(System.Linq.Enumerable.Repeat("[.", 20_000)) + string.Concat(System.Linq.Enumerable.Repeat("]", 20_000));
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { new XQueryParserFacade().Parse(query); }
#pragma warning disable CA1031 // the outcome is examined below
            catch (Exception e) { caught = e; }
#pragma warning restore CA1031
        }, 1024 * 1024);
        thread.Start();
        thread.Join();

        caught.Should().BeOfType<XQueryParseException>().Which.Message.Should().Contain("nesting depth");
    }
}
