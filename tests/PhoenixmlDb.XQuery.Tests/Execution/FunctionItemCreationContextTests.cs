using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// XQueryFunction.BindCreationContext is called whenever a function item is created from a named
/// function, so a function whose result depends on the static context where it is NAMED can
/// capture that context. XSLT's system-property() needs it: an item created in the stylesheet and
/// invoked inside xsl:evaluate must resolve 'xsl:version' against the stylesheet's prefixes.
/// </summary>
public sealed class FunctionItemCreationContextTests
{
    private sealed class CreationProbe(bool bound) : PhoenixmlDb.XQuery.Ast.XQueryFunction
    {
        public override QName Name => new(FunctionNamespaces.Fn, "creation-probe");
        public override PhoenixmlDb.XQuery.Ast.XdmSequenceType ReturnType => PhoenixmlDb.XQuery.Ast.XdmSequenceType.String;
        public override IReadOnlyList<PhoenixmlDb.XQuery.Ast.FunctionParameterDef> Parameters =>
            [new() { Name = new QName(NamespaceId.None, "x"), Type = PhoenixmlDb.XQuery.Ast.XdmSequenceType.OptionalItem }];

        public override PhoenixmlDb.XQuery.Ast.XQueryFunction BindCreationContext(PhoenixmlDb.XQuery.Ast.ExecutionContext context)
            => new CreationProbe(bound: true);

        public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, PhoenixmlDb.XQuery.Ast.ExecutionContext context)
            => ValueTask.FromResult<object?>(bound ? "bound" : "unbound");
    }

    private static async Task<string?> RunAsync(string query)
    {
        var lib = FunctionLibrary.Standard.Copy();
        lib.Register(new CreationProbe(bound: false));
        await foreach (var item in new QueryEngine(functions: lib).ExecuteAsync(query))
            return item?.ToString();
        return null;
    }

    [Fact]
    public async Task A_static_call_does_not_create_an_item()
        => (await RunAsync("fn:creation-probe(1)")).Should().Be("unbound");

    [Theory]
    [InlineData("fn:creation-probe#1(1)")]
    [InlineData("let $f := fn:creation-probe#1 return $f(1)")]
    [InlineData("fn:creation-probe(?)(1)")]
    [InlineData("function-lookup(xs:QName('fn:creation-probe'), 1)(1)")]
    public async Task Every_way_of_creating_an_item_binds_the_creation_context(string query)
        => (await RunAsync(query)).Should().Be("bound");
}
