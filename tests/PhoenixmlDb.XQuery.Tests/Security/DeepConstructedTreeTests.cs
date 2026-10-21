using AwesomeAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Security;

/// <summary>
/// A tree the query builds itself, nested deeper than a small thread stack can recurse, must not
/// overflow the stack: a .NET StackOverflowException ends the process and cannot be caught. Each
/// operation that walked the tree with one call per level now continues on a fresh stack
/// (StackGuard), or copies with an explicit stack (DeepCopyNode). Run on a 256 KB thread, where
/// a few hundred levels were enough.
/// </summary>
public class DeepConstructedTreeTests
{
    private const string Build = "let $t := fold-left(1 to 1500, <leaf>x</leaf>, function($inner, $i) { <d a='{$i}'>{$inner}</d> }) return ";

    [Theory]
    [InlineData("count($t//d)", "1499")]
    [InlineData("string-length(serialize($t)) gt 0", "true")]
    [InlineData("string-length(serialize($t, map{'method':'html'})) gt 0", "true")]
    [InlineData("string-length(serialize($t, map{'method':'xhtml'})) gt 0", "true")]
    [InlineData("deep-equal($t, $t)", "true")]
    [InlineData("string(base-uri($t//leaf))", "")]
    [InlineData("count(copy $c := $t modify () return $c//d)", "1499")]
    public void A_deep_tree_built_by_the_query_does_not_overflow_a_small_stack(string query, string expected)
    {
        string? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
#pragma warning disable CA1031 // reported through the assertion below
            try { result = new XQueryFacade().EvaluateAsync(Build + query, inputXml: null).GetAwaiter().GetResult(); }
            catch (Exception ex) { failure = ex; }
#pragma warning restore CA1031
        }, maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        failure.Should().BeNull();
        result.Should().Be(expected);
    }
}
