using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// array:subarray($a, $start, $length): FOAY0002 for a negative length, FOAY0001 for a start
/// or an end outside the array. A negative length was FOAY0001 (QT3 array-subarray-311), and a
/// start beyond the int range overflowed the end check and returned [] (array-subarray-317).
/// </summary>
public class ArraySubarrayBoundsTests
{
    private static async Task<string> Eval(string query)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));
        var items = new List<object?>();
        await foreach (var i in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(i);
        return string.Join(",", items.Select(i => i?.ToString() ?? ""));
    }

    [Theory]
    [InlineData("array:subarray([1,2,3], 2, -1)", "FOAY0002")]
    [InlineData("array:subarray([1,2,3], 1, -4294967297)", "FOAY0002")]
    [InlineData("array:subarray([1,2,3,4,5], 4294967297, 2)", "FOAY0001")]
    [InlineData("array:subarray([1,2,3,4,5], 2147483647, 2)", "FOAY0001")]
    [InlineData("array:subarray([1,2,3], 2, 4294967297)", "FOAY0001")]
    [InlineData("array:subarray([1,2,3], 0, 1)", "FOAY0001")]
    [InlineData("array:subarray([1,2,3], 5, -1)", "FOAY0001")]
    public async Task An_out_of_range_argument_raises_its_code(string query, string expected)
    {
        var act = () => Eval(query);
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be(expected);
    }

    [Theory]
    [InlineData("array:size(array:subarray([1,2,3], 2, 2))", "2")]
    [InlineData("array:size(array:subarray([1,2,3], 4, 0))", "0")]
    [InlineData("array:subarray([1,2,3], 3, 1)?1", "3")]
    public async Task An_in_range_subarray_is_returned(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
