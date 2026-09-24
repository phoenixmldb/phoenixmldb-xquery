using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using PhoenixmlDb.XQuery;
using PhoenixmlDb.XQuery.Execution;
using Xunit;

namespace PhoenixmlDb.XQuery.Cli.Tests;

/// <summary>
/// The CLI carries its OWN <see cref="ResultSerializer"/> alongside the engine's
/// <c>XQueryResultSerializer</c>, and it had no case for a function item at all — so one fell
/// through to <c>item.ToString()</c> and the CLI printed a .NET type name to the user:
///
/// <code>
/// $ echo 'function($x){$x}' > q.xq &amp;&amp; xquery -f q.xq
/// PhoenixmlDb.XQuery.Execution.InlineFunctionItem
/// </code>
///
/// A type name is not a wrong value — it is an internal detail crossing the process boundary, and
/// it tells the reader the query worked where the correct answer is either the adaptive rendering
/// or a serialization error. Two serializers for one job with the rule in only one of them.
/// </summary>
public sealed class CliFunctionItemSerializationTests
{
    /// <summary>
    /// The CLI's <c>OutputMethod</c> is <c>internal</c> — a THIRD copy of this concept, after the
    /// engine's enum and the serializer's own switch — so the theories name the method as a string
    /// rather than exposing the enum in a public signature.
    /// </summary>
    private static OutputMethod Method(string name) => name switch
    {
        "adaptive" => OutputMethod.Adaptive,
        "xml" => OutputMethod.Xml,
        "text" => OutputMethod.Text,
        "json" => OutputMethod.Json,
        _ => throw new System.ArgumentOutOfRangeException(nameof(name), name, "unknown output method"),
    };

    private static async Task<string> Run(string query, OutputMethod method)
    {
        var store = new XdmDocumentStore();
        var engine = new QueryEngine(nodeProvider: store, documentResolver: store);
        var compiled = engine.Compile(query);
        compiled.Success.Should().BeTrue(string.Join("; ", compiled.Errors));

        var items = new List<object?>();
        await foreach (var item in compiled.ExecutionPlan!.ExecuteAsync(engine.CreateContext()))
            items.Add(item);

        using var writer = new StringWriter();
        new ResultSerializer(store, writer, method).Serialize(items.Count == 1 ? items[0] : items.ToArray());
        return writer.ToString();
    }

    /// <summary>
    /// Serialization 4.0 §6: an inline function is <c>(anonymous-function)#arity</c>. This is the
    /// CLI's DEFAULT method, so it is the output a user sees without passing -o at all.
    /// </summary>
    [Fact]
    public async Task Adaptive_renders_an_inline_function_per_the_spec()
        => (await Run("function($x){$x}", OutputMethod.Adaptive)).Should().Be("(anonymous-function)#1");

    /// <summary>And a named function reference is prefix:local#arity.</summary>
    [Fact]
    public async Task Adaptive_renders_a_named_function_reference_per_the_spec()
        => (await Run("fn:concat#2", OutputMethod.Adaptive)).Should().Be("fn:concat#2");

    /// <summary>
    /// The XML method has no lexical form for a function item: SENR0001. That is what the engine
    /// already answers for <c>fn:serialize(fn:name#1)</c> and what QT3 <c>serialize-xml-010</c>
    /// requires, so the CLI disagreeing with it was the defect.
    /// </summary>
    [Theory]
    [InlineData("xml")]
    [InlineData("text")]
    public async Task Non_adaptive_methods_raise_SENR0001(string method)
    {
        var act = async () => await Run("function($x){$x}", Method(method));
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("SENR0001");
    }

    /// <summary>The JSON method has its own code for an item it cannot represent.</summary>
    [Fact]
    public async Task Json_raises_SERE0023()
    {
        var act = async () => await Run("function($x){$x}", OutputMethod.Json);
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("SERE0023");
    }

    /// <summary>
    /// Guards. Maps and arrays reach their own case arms and were never affected — checked rather
    /// than assumed, because "only function items are affected" is the kind of claim that is
    /// usually made by looking at one example.
    /// </summary>
    [Theory]
    [InlineData("map{\"a\":1}", "map{\"a\":1}")]
    [InlineData("array{1,2}", "[1,2]")]
    [InlineData("1+1", "2")]
    public async Task Other_item_kinds_are_unchanged(string query, string expected)
        => (await Run(query, OutputMethod.Adaptive)).Should().Be(expected);

    /// <summary>
    /// The type name must not appear under ANY method — the assertion the whole fix exists for,
    /// and the one that would have caught this originally.
    /// </summary>
    [Theory]
    [InlineData("adaptive")]
    [InlineData("xml")]
    [InlineData("text")]
    [InlineData("json")]
    public async Task No_dotnet_type_name_ever_reaches_the_output(string method)
    {
        string output;
        try { output = await Run("function($x){$x}", Method(method)); }
        catch (XQueryRuntimeException) { return; }   // an error is an acceptable answer; a type name is not
        output.Should().NotContain("PhoenixmlDb.", "a .NET type name must never reach a user");
    }
}
