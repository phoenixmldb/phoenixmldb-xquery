using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// An array member that a built-in function produced is held as a one-item sequence. deep-equal
/// compared it with an atomic member as a sequence against a number, and said false
/// (QT3 d1e57818).
/// </summary>
public class DeepEqualSingletonMemberTests
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
    [InlineData("deep-equal(array:for-each(['a', 'b', 'c'], string-to-codepoints(?)), [97, 98, 99])", "true")]
    [InlineData("deep-equal([97, 98], array:for-each(['a', 'b'], string-to-codepoints#1))", "true")]
    [InlineData("deep-equal(map { 1: string-to-codepoints('a') }, map { 1: 97 })", "true")]
    [InlineData("deep-equal(array:for-each(['a'], string-to-codepoints#1), [98])", "false")]
    [InlineData("deep-equal(array:for-each(['ab'], string-to-codepoints#1), [97])", "false")]
    [InlineData("deep-equal(array:for-each([''], string-to-codepoints#1), [()])", "true")]
    public async Task A_one_item_member_is_its_item(string query, string expected)
        => (await Eval($"string({query})")).Should().Be(expected);
}
