using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:adjust-time-to-timezone($arg as xs:time?) as xs:time?</summary>
public sealed class AdjustTimeToTimezoneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "adjust-time-to-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var xt = DateTimeHelper.ParseXsTime(arg);
        var implicitTz = DateTimeOffset.Now.Offset;
        return ValueTask.FromResult<object?>((object)DateTimeHelper.AdjustTimeToTimezone(xt, implicitTz));
    }
}
