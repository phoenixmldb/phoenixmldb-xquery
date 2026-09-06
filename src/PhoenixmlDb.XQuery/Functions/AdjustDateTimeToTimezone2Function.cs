using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:adjust-dateTime-to-timezone($arg, $timezone) as xs:dateTime?</summary>
public sealed class AdjustDateTimeToTimezone2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "adjust-dateTime-to-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem },
         new() { Name = new QName(NamespaceId.None, "timezone"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var tz = arguments[1];
        TimeSpan? offset = tz is null ? null : tz is TimeSpan ts ? ts : TimeSpan.Parse(tz.ToString()!);
        if (offset.HasValue) DateTimeHelper.ValidateTimezoneOffset(offset.Value);
        return ValueTask.FromResult<object?>(AdjustDateTimeToTimezoneFunction.AdjustDateTime(arg, offset));
    }
}
