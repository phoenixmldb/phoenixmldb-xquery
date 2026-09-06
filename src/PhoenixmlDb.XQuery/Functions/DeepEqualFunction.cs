using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:deep-equal($arg1, $arg2) as xs:boolean
/// </summary>
public sealed class DeepEqualFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "deep-equal");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "parameter1"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "parameter2"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var comparison = CollationHelper.GetDefaultComparison(context);
        return DeepEqualWithComparison(arguments[0], arguments[1], comparison, context.NodeStore);
    }

    internal static ValueTask<object?> DeepEqualWithComparison(object? arg1, object? arg2, StringComparison comparison,
        INodeStore? nodeStore = null, Ast.ExecutionContext? context = null)
    {
        using var enumA = ToEnumerable(arg1).GetEnumerator();
        using var enumB = ToEnumerable(arg2).GetEnumerator();

        while (true)
        {
            var hasA = enumA.MoveNext();
            var hasB = enumB.MoveNext();

            if (!hasA && !hasB)
                return ValueTask.FromResult<object?>(true);
            if (hasA != hasB)
                return ValueTask.FromResult<object?>(false);
            // FOTY0015: function items cannot be compared with deep-equal
            if (enumA.Current is Ast.XQueryFunction || enumB.Current is Ast.XQueryFunction)
                throw context.Error("FOTY0015", "fn:deep-equal cannot be applied to function items");
            if (!Execution.TypeCastHelper.DeepEquals(enumA.Current, enumB.Current, comparison, nodeStore))
                return ValueTask.FromResult<object?>(false);
        }
    }

    private static IEnumerable<object?> ToEnumerable(object? arg, Ast.ExecutionContext? context = null)
    {
        // XDM arrays (List<object?>) are single items — do not flatten into their members.
        // deep-equal([1], 1) must be false: an array is never equal to an atomic value.
        if (arg is List<object?>)
            return [arg];
        if (arg is IEnumerable<object?> seq)
            return seq;
        if (arg == null)
            return Enumerable.Empty<object?>();
        return [arg];
    }
}
