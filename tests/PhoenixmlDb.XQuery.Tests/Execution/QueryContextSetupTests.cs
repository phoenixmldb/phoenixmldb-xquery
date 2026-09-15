using FluentAssertions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.XQuery.Functions;
using Xunit;

namespace PhoenixmlDb.XQuery.Tests.Execution;

/// <summary>
/// QueryExecutionContext creates most of its state on first use and reads the clock once, and a function call site
/// keeps what it resolved until the library changes. These pin the behaviour that must not move with that: the
/// current instant and implicit timezone are single values per query, focus nesting restores position and size,
/// and a registration made after a call site first ran is still seen.
/// </summary>
public class QueryContextSetupTests
{
    private readonly XQueryFacade _facade = new();

    [Fact]
    public async Task CurrentDateTime_is_one_value_throughout_a_query()
    {
        var result = await _facade.EvaluateAsync(
            "count(distinct-values(for $i in 1 to 5000 return string(current-dateTime())))");
        result.Should().Be("1");
    }

    [Fact]
    public async Task ImplicitTimezone_agrees_with_current_dateTime()
    {
        var result = await _facade.EvaluateAsync(
            "implicit-timezone() eq timezone-from-dateTime(current-dateTime())");
        result.Should().Be("true");
    }

    [Fact]
    public async Task Inner_focus_restores_outer_position_and_size()
    {
        var result = await _facade.EvaluateAsync(
            "string-join((1 to 3) ! (count(('x', 'y') ! position()) || ':' || position() || '/' || last()), ' ')");
        result.Should().Be("2:1/3 2:2/3 2:3/3");
    }

    [Fact]
    public async Task Call_site_resolves_again_after_the_library_changes()
    {
        var library = new FunctionLibrary();
        library.Register(new StringLengthFunction());
        var call = new FunctionCallOperator
        {
            FunctionName = new QName(FunctionNamespaces.Fn, "string-length"),
            ArgumentOperators = [new ConstantOperator { Value = "abc" }],
        };

        (await RunAsync(call, library)).Should().Equal(3L);

        // Same call site, same library instance, a later registration under the same name and arity.
        library.Register(new FixedStringLength(42));
        (await RunAsync(call, library)).Should().Equal(42L);

        // A different library resolves in that library.
        (await RunAsync(call, FunctionLibrary.Standard)).Should().Equal(3L);
    }

    [Fact]
    public async Task Shared_call_site_uses_each_executions_own_library_under_concurrency()
    {
        // One plan executed in parallel by contexts whose libraries resolve the same name differently — the shape of
        // concurrent XSLT transformations of one compiled stylesheet, each with its own function library.
        var one = new FunctionLibrary();
        one.Register(new FixedStringLength(1));
        var two = new FunctionLibrary();
        two.Register(new FixedStringLength(2));
        var call = new FunctionCallOperator
        {
            FunctionName = new QName(FunctionNamespaces.Fn, "string-length"),
            ArgumentOperators = [new ConstantOperator { Value = "abc" }],
        };

        var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(async () =>
        {
            var (library, expected) = worker % 2 == 0 ? (one, 1L) : (two, 2L);
            for (var i = 0; i < 2000; i++)
            {
                var items = await RunAsync(call, library);
                if (items.Count != 1 || !Equals(items[0], expected))
                    return $"worker {worker} iteration {i}: expected {expected}, got [{string.Join(", ", items)}]";
            }
            return null;
        })).ToArray();

        (await Task.WhenAll(workers)).Where(failure => failure != null).Should().BeEmpty();
    }

    private static async Task<List<object?>> RunAsync(PhysicalOperator op, FunctionLibrary library)
    {
        using var context = new QueryExecutionContext(default, functions: library);
        var items = new List<object?>();
        await foreach (var item in op.ExecuteAsync(context))
            items.Add(item);
        return items;
    }

    private sealed class FixedStringLength(long value) : XQueryFunction
    {
        public override QName Name => new(FunctionNamespaces.Fn, "string-length");
        public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
        public override IReadOnlyList<FunctionParameterDef> Parameters =>
            [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

        public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, global::PhoenixmlDb.XQuery.Ast.ExecutionContext context)
            => ValueTask.FromResult<object?>(value);
    }
}
