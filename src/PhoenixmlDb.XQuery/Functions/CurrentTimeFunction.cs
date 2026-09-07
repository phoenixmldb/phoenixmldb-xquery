using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:current-time() as xs:time
/// </summary>
public sealed class CurrentTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "current-time");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Time, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var now = context is Execution.QueryExecutionContext qec
            ? qec.CurrentDateTime
            : DateTimeOffset.Now;
        var fracTicks = (int)(now.Ticks % TimeSpan.TicksPerSecond);
        return ValueTask.FromResult<object?>(new Xdm.XsTime(TimeOnly.FromDateTime(now.DateTime), now.Offset, fracTicks));
    }
}
