using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:timezone-from-dateTime($arg as xs:dateTime?) as xs:dayTimeDuration?</summary>
public sealed class TimezoneFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "timezone-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDateTime xdt)
            return ValueTask.FromResult<object?>(xdt.HasTimezone ? (object)xdt.Value.Offset : null);
        if (arg is DateTimeOffset dto)
            return ValueTask.FromResult<object?>((object)dto.Offset);
        var parsed = Xdm.XsDateTime.Parse(arg.ToString()!);
        return ValueTask.FromResult<object?>(parsed.HasTimezone ? (object)parsed.Value.Offset : null);
    }
}
