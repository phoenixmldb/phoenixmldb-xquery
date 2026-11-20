using AwesomeAssertions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Inserting and replacing attributes changes the element's attributes (BUGS.md #126).
/// </summary>
/// <remarks>
/// Inserted attributes used to be appended to the element's CHILD list, where neither
/// navigation (<c>$c/@n</c>) nor the serializer saw them, and <c>replace node</c> on an attribute
/// looked for it among the children, did not find it, and did nothing. Both were silent.
/// </remarks>
public class AttributeUpdateTests
{
    private readonly XQueryFacade _facade = new();

    private Task<string> Run(string update)
        => _facade.EvaluateAsync($$"""serialize(copy $c := <e a="1"><k/></e> modify ({{update}}) return $c)""");

    [Theory]
    [InlineData("""insert node attribute n {"2"} into $c""", """<e a="1" n="2"><k/></e>""")]
    [InlineData("""insert node attribute n {"2"} as first into $c""", """<e a="1" n="2"><k/></e>""")]
    [InlineData("""insert node attribute n {"2"} as last into $c""", """<e a="1" n="2"><k/></e>""")]
    [InlineData("""insert node attribute n {"2"} before $c/k""", """<e a="1" n="2"><k/></e>""")]
    [InlineData("""insert node attribute n {"2"} after $c/k""", """<e a="1" n="2"><k/></e>""")]
    [InlineData("""insert node attribute n {"2"} into $c/k""", """<e a="1"><k n="2"/></e>""")]
    [InlineData("""insert node (attribute n {"2"}, <z/>) into $c""", """<e a="1" n="2"><k/><z/></e>""")]
    [InlineData("""replace node $c/@a with attribute n {"2"}""", """<e n="2"><k/></e>""")]
    [InlineData("""replace node $c/@a with (attribute n {"2"}, attribute m {"3"})""", """<e n="2" m="3"><k/></e>""")]
    public async Task Attribute_update_changes_the_attributes(string update, string expected)
    {
        (await Run(update)).Should().Be(expected);
    }

    [Fact]
    public async Task Inserted_attribute_is_an_attribute_of_the_element_and_not_a_child()
    {
        const string query = """
            let $doc := document { <r><e/><x k="v"/></r> }
            let $c := copy $c := $doc/r modify insert node $doc/r/x/@k into $c/e return $c
            return string-join((
              "attr:" || ($c/e/@k = "v"),
              "no-child:" || empty($c/e/node()),
              "parent:" || ($c/e/@k/.. is $c/e),
              "new-identity:" || not($c/e/@k is $doc/r/x/@k),
              "orig-parent:" || ($doc/r/x/@k/.. is $doc/r/x)
            ), " ")
            """;
        (await _facade.EvaluateAsync(query)).Should().Be("attr:true no-child:true parent:true new-identity:true orig-parent:true");
    }

    [Fact]
    public async Task Inserted_attribute_into_a_constructed_element_without_attributes()
    {
        (await _facade.EvaluateAsync("""copy $c := <e/> modify insert node attribute n {"2"} into $c return string($c/@n)"""))
            .Should().Be("2");
    }
}
