using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:timezone-from-date($arg as xs:date?) as xs:dayTimeDuration?</summary>
public sealed class TimezoneFromDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "timezone-from-date");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDate xd)
            return ValueTask.FromResult<object?>(xd.Timezone.HasValue ? (object)xd.Timezone.Value : null);
        if (arg is DateOnly) return ValueTask.FromResult<object?>(null);
        if (arg is DateTimeOffset dto)
            return ValueTask.FromResult<object?>((object)dto.Offset);
        var parsed = Xdm.XsDate.Parse(arg.ToString()!);
        return ValueTask.FromResult<object?>(parsed.Timezone.HasValue ? (object)parsed.Timezone.Value : null);
    }
}
