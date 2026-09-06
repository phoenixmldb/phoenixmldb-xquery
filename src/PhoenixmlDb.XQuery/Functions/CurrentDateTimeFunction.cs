using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:current-dateTime() as xs:dateTime
/// </summary>
public sealed class CurrentDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "current-dateTime");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.DateTime, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Read from the execution context — fresh per query, stable within a query
        var now = context is Execution.QueryExecutionContext qec
            ? qec.CurrentDateTime
            : DateTimeOffset.Now;
        return ValueTask.FromResult<object?>(new Xdm.XsDateTime(now, true));
    }
}
