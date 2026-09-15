using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:implicit-timezone() as xs:dayTimeDuration</summary>
public sealed class ImplicitTimezoneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "implicit-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        // Part of the dynamic context, like the current date and time: taken from the instant the context captured,
        // so it cannot change during a query and agrees with fn:current-dateTime().
        var now = context is Execution.QueryExecutionContext qec ? qec.CurrentDateTime : DateTimeOffset.Now;
        return ValueTask.FromResult<object?>((object)now.Offset);
    }
}
