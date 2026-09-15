using FluentAssertions;
using PhoenixmlDb.XQuery;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests;

/// <summary>
/// <c>declare option output:item-separator</c> was never read from the prolog. The serializer honoured
/// SerializationOptions.ItemSeparator, but DetectSerializationOptions had no case for it, so the
/// separator was dropped: QT3 K2-Serialization-13 got 12345678910 for <c>1 to 10</c> under "|".
/// The facade serializes each top-level item separately, so it must write the separator itself.
/// </summary>
public class ItemSeparatorOptionTests
{
    private const string Output = "declare namespace output = \"http://www.w3.org/2010/xslt-xquery-serialization\"; ";

    private static Task<string> Eval(string query) => new XQueryFacade().EvaluateAsync(query);

    [Fact]
    public void Prolog_item_separator_is_detected()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:item-separator \"|\"; 1 to 3")
            .ItemSeparator.Should().Be("|");

    [Fact]
    public void No_declared_item_separator_leaves_it_unset()
        => XQueryFacade.DetectSerializationOptions(Output + "declare option output:method \"text\"; 1 to 3")
            .ItemSeparator.Should().BeNull();

    [Fact]
    public async Task Text_method_writes_declared_separator_between_items()
        => (await Eval(Output + "declare option output:method \"text\"; declare option output:item-separator \"|\"; <a>{1,2,3}</a>,<b>{4,5,6}</b>"))
            .Should().Be("1 2 3|4 5 6");

    [Fact]
    public async Task Adaptive_method_writes_declared_separator_between_items()
        => (await Eval(Output + "declare option output:item-separator \", \"; 1 to 3"))
            .Should().Be("1, 2, 3");

    // Guard: without a declaration the facade's output is unchanged.
    [Fact]
    public async Task Without_a_declared_separator_items_concatenate_as_before()
        => (await Eval("1 to 3")).Should().Be("123");
}
