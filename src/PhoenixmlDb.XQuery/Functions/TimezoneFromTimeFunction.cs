using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:timezone-from-time($arg as xs:time?) as xs:dayTimeDuration?</summary>
public sealed class TimezoneFromTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "timezone-from-time");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsTime xt)
            return ValueTask.FromResult<object?>(xt.Timezone.HasValue ? (object)xt.Timezone.Value : null);
        if (arg is DateTimeOffset dto)
            return ValueTask.FromResult<object?>((object)dto.Offset);
        var parsed = Xdm.XsTime.Parse(arg.ToString()!);
        return ValueTask.FromResult<object?>(parsed.Timezone.HasValue ? (object)parsed.Timezone.Value : null);
    }
}
