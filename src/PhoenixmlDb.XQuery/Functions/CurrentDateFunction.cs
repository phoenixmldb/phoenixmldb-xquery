using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:current-date() as xs:date
/// </summary>
public sealed class CurrentDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "current-date");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Date, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var now = context is Execution.QueryExecutionContext qec
            ? qec.CurrentDateTime
            : DateTimeOffset.Now;
        return ValueTask.FromResult<object?>(new Xdm.XsDate(DateOnly.FromDateTime(now.DateTime), now.Offset));
    }
}
