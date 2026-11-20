using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Insert and replace place COPIES of their source nodes (XQUF 3.0 §3.2.1, §3.4.1), never the
/// source objects themselves (BUGS.md #125).
/// </summary>
/// <remarks>
/// The source objects used to be placed as they were and re-parented: after
/// <c>insert node $orig/x into $c/e</c>, <c>$c/e/x is $orig/x</c> was true and <c>$orig/x/..</c>
/// was the copy's <c>e</c>, so the input document had silently changed. Child lists looked
/// unchanged; only upward navigation and identity showed it. Each check below is a boolean so
/// that a failure names the property that broke.
/// </remarks>
public class UpdateContentCopyTests
{
    private readonly XQueryFacade _facade = new();

    private const string Source = """<r><e><a/><b>old</b></e><x k="v">src<y/></x></r>""";

    // Each primitive places one copy of $orig/x under $c/e.
    [Theory]
    [InlineData("insert node $orig/x into $c/e")]
    [InlineData("insert node $orig/x as first into $c/e")]
    [InlineData("insert node $orig/x as last into $c/e")]
    [InlineData("insert node $orig/x before $c/e/a")]
    [InlineData("insert node $orig/x after $c/e/b")]
    [InlineData("replace node $c/e/b with $orig/x")]
    public async Task Placed_element_is_a_new_node_and_the_original_is_unchanged(string update)
    {
        var query = $$"""
            let $doc := document { {{Source}} }
            let $orig := $doc/r
            let $c := copy $c := $orig modify ({{update}}) return $c
            let $n := $c/e/x
            return string-join((
              "one:" || (count($n) = 1),
              "new-identity:" || not($n is $orig/x),
              "parent-is-copy:" || ($n/.. is $c/e),
              "root-is-copy:" || (root($n) is $c),
              "child-new-identity:" || not($n/y is $orig/x/y),
              "child-parent:" || ($n/y/.. is $n),
              "attribute-parent:" || ($n/@k/.. is $n),
              "ids-differ:" || (generate-id($n) ne generate-id($orig/x)),
              "orig-parent:" || ($orig/x/.. is $orig),
              "orig-root:" || (root($orig/x) is $doc),
              "orig-child-parent:" || ($orig/x/y/.. is $orig/x),
              "orig-unchanged:" || deep-equal($doc, document { {{Source}} })
            ), " ")
            """;
        (await _facade.EvaluateAsync(query)).Should().Be(
            "one:true new-identity:true parent-is-copy:true root-is-copy:true child-new-identity:true " +
            "child-parent:true attribute-parent:true ids-differ:true orig-parent:true orig-root:true " +
            "orig-child-parent:true orig-unchanged:true");
    }

    [Fact]
    public async Task Source_from_the_copy_itself_is_copied_and_stays_where_it_was()
    {
        const string query = """
            let $c := copy $c := <r><e/><x>s</x></r> modify insert node $c/x into $c/e return $c
            return string-join((
              "placed:" || (count($c/e/x) = 1),
              "new-identity:" || not($c/e/x is $c/x),
              "placed-parent:" || ($c/e/x/.. is $c/e),
              "source-stays:" || (count($c/x) = 1 and $c/x/.. is $c)
            ), " ")
            """;
        (await _facade.EvaluateAsync(query)).Should().Be("placed:true new-identity:true placed-parent:true source-stays:true");
    }

    [Fact]
    public async Task Copy_is_taken_when_the_update_is_evaluated()
    {
        // The insertion sequence is copied when the insert expression is evaluated, so a later
        // primitive of the same modify clause that changes the source does not reach the copy.
        const string query = """
            let $c := copy $c := <r><e/><x>s</x></r>
                      modify (insert node $c/x into $c/e, insert node <z/> into $c/x)
                      return $c
            return string-join(("placed-has-z:" || exists($c/e/x/z), "source-has-z:" || exists($c/x/z)), " ")
            """;
        (await _facade.EvaluateAsync(query)).Should().Be("placed-has-z:false source-has-z:true");
    }
}
