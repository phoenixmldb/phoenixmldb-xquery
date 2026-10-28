using AwesomeAssertions;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// index-of, distinct-values and the other value-equality functions compare xs:QName values by
/// local name and namespace URI, as eq does. They compared namespace ids, so a QName from the
/// prolog's binding of a prefix missed the same name from a constructor that bound the prefix
/// itself (QT3 functx-functx-change-element-names-deep-3, -deep-all).
/// </summary>
public class QNameValueEqualityTests
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

    private const string Dty = "declare namespace dty = 'http://datypic.com'; ";
    private const string Name = "node-name(<x xmlns:dty='http://datypic.com'><dty:b/></x>/*)";

    [Theory]
    [InlineData(Dty + "index-of((xs:QName('c'), xs:QName('dty:b')), " + Name + ")", "2")]
    [InlineData(Dty + "count(distinct-values((xs:QName('dty:b'), " + Name + ")))", "1")]
    [InlineData("index-of(QName('http://datypic.com', 'b'), " + Name + ")", "1")]
    [InlineData(Dty + "index-of(QName('http://other', 'dty:b'), " + Name + ")", "")]
    [InlineData(Dty + "index-of(xs:QName('dty:c'), " + Name + ")", "")]
    public async Task QNames_compare_by_namespace_URI(string query, string expected)
        => (await Eval(query)).Should().Be(expected);
}
