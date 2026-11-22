using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// Updates of one list must not conflict, and a modify clause may change only what its copy
/// clause made (XQUF 3.0; BUGS.md #128). All of these were silent: the last rename won, the first
/// replacement won, the last value won, and a change of a node the copy did not make was ignored.
/// </summary>
public class UpdateConflictTests
{
    private readonly XQueryFacade _facade = new();

    public static TheoryData<string, string> CopyModify => new()
    {
        { "let $o := <r><a/></r> return copy $c := $o modify delete node $o/a return $c", "XUDY0014" },
        { "copy $c := <r><a/></r> modify (rename node $c/a as 'x', rename node $c/a as 'y') return $c", "XUDY0015" },
        { "copy $c := <r><a/></r> modify (replace node $c/a with <x/>, replace node $c/a with <y/>) return $c", "XUDY0016" },
        { "copy $c := <r><a/></r> modify (replace value of node $c/a with '1', replace value of node $c/a with '2') return $c", "XUDY0017" },
        { "copy $c := <r a='0'/> modify (replace value of node $c/@a with '1', replace value of node $c/@a with '2') return $c", "XUDY0017" },
    };

    [Theory]
    [MemberData(nameof(CopyModify))]
    public async Task ACopyModifyThatConflictsIsAnError(string query, string code)
    {
        var act = () => _facade.EvaluateAsync(query);

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be(code);
    }

    [Theory]
    [InlineData("copy $c := <r><a/></r> modify (rename node $c/a as 'x', replace value of node $c/a with '1') return string($c/x)", "1")]
    [InlineData("copy $c := <r><a/><b/></r> modify (rename node $c/a as 'x', rename node $c/b as 'y') return string-join($c/*!name(), ' ')", "x y")]
    [InlineData("copy $c := <r><a/></r> modify (insert node <x/> into $c/a, insert node <y/> into $c/a) return count($c/a/*)", "2")]
    public async Task UpdatesThatDoNotConflictAreApplied(string query, string expected)
        => (await _facade.EvaluateAsync(query)).Should().Be(expected);

    [Fact]
    public async Task ATopLevelConflictIsAnErrorAndNothingChanges()
    {
        var store = new XdmDocumentStore();
        store.LoadFromString("<r><a/></r>", "urn:test:d");
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile("(rename node doc('urn:test:d')/r/a as 'x', rename node doc('urn:test:d')/r/a as 'y')");

        var act = async () =>
        {
            await foreach (var _ in engine.ExecuteAsync(compiled.ExecutionPlan!))
            {
            }
        };

        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XUDY0015");
        var check = engine.Compile("string-join(doc('urn:test:d')/r/*!name(), ' ')");
        var names = new List<object?>();
        await foreach (var item in engine.ExecuteAsync(check.ExecutionPlan!))
            names.Add(item);
        names.Should().Equal("a");
    }
}
