using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// An <c>external</c> function declaration binds to the function the host registered under the
/// same name and arity; with none registered it is XPST0017 (#18). It used to get an empty body,
/// so an unbound external function silently returned (), or failed its return-type check with a
/// message about the empty sequence.
/// </summary>
public sealed class ExternalFunctionBindingTests
{
    private sealed class HostGreeting : PhoenixmlDb.XQuery.Ast.XQueryFunction
    {
        public override QName Name => new(FunctionNamespaces.Local, "greet");
        public override PhoenixmlDb.XQuery.Ast.XdmSequenceType ReturnType => PhoenixmlDb.XQuery.Ast.XdmSequenceType.String;
        public override IReadOnlyList<PhoenixmlDb.XQuery.Ast.FunctionParameterDef> Parameters =>
            [new() { Name = new QName(NamespaceId.None, "who"), Type = PhoenixmlDb.XQuery.Ast.XdmSequenceType.String }];

        public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, PhoenixmlDb.XQuery.Ast.ExecutionContext context)
            => ValueTask.FromResult<object?>("hello " + arguments[0]);
    }

    private static async Task<List<string?>> RunAsync(string query, bool bind)
    {
        var lib = FunctionLibrary.Standard.Copy();
        if (bind) lib.Register(new HostGreeting());
        var results = new List<string?>();
        await foreach (var item in new QueryEngine(functions: lib).ExecuteAsync(query))
            results.Add(item?.ToString());
        return results;
    }

    [Theory]
    [InlineData("declare function local:greet($who as xs:string) as xs:string external; local:greet('db')")]
    [InlineData("declare function local:greet($who as xs:string) external; local:greet('db')")]
    [InlineData("declare function local:greet($who) external; local:greet#1('db')")]
    public async Task A_host_registered_function_implements_the_external_declaration(string query)
        => (await RunAsync(query, bind: true)).Should().Equal("hello db");

    [Theory]
    [InlineData("declare function local:greet($who as xs:string) as xs:string external; local:greet('db')")]
    // No return type: this one silently returned the empty sequence.
    [InlineData("declare function local:greet($who) external; count(local:greet('db'))")]
    // Declared but never called is still unbound.
    [InlineData("declare function local:greet($who) external; 1")]
    public async Task An_unbound_external_function_is_XPST0017(string query)
    {
        var act = async () => await RunAsync(query, bind: false);
        var ex = await act.Should().ThrowAsync<XQueryRuntimeException>();
        ex.Which.ErrorCode.Should().Be("XPST0017");
        ex.Which.Message.Should().Contain("greet#1");
    }

    /// <summary>A host function of another arity does not bind it.</summary>
    [Fact]
    public async Task Binding_needs_the_same_arity()
    {
        var act = async () => await RunAsync("declare function local:greet() external; local:greet()", bind: true);
        (await act.Should().ThrowAsync<XQueryRuntimeException>()).Which.ErrorCode.Should().Be("XPST0017");
    }
}
