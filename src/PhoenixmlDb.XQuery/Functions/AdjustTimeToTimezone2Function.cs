using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:adjust-time-to-timezone($arg, $timezone) as xs:time?</summary>
public sealed class AdjustTimeToTimezone2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "adjust-time-to-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem },
         new() { Name = new QName(NamespaceId.None, "timezone"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var xt = DateTimeHelper.ParseXsTime(arg);
        var tz = arguments[1];
        if (tz is null)
        {
            // Remove timezone: return time with no timezone
            return ValueTask.FromResult<object?>((object)new Xdm.XsTime(xt.Time, null, xt.FractionalTicks));
        }
        var offset = tz is TimeSpan ts ? ts : TimeSpan.Parse(tz.ToString()!);
        DateTimeHelper.ValidateTimezoneOffset(offset);
        return ValueTask.FromResult<object?>((object)DateTimeHelper.AdjustTimeToTimezone(xt, offset));
    }
}
