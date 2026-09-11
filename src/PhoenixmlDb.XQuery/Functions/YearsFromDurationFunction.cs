using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:years-from-duration($arg as xs:duration?) as xs:integer?</summary>
public sealed class YearsFromDurationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "years-from-duration");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.YearMonthDuration ymd)
            return ValueTask.FromResult<object?>((long)ymd.Years);
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>((long)dur.Years);
        // xs:dayTimeDuration (TimeSpan) has no year component
        if (arg is TimeSpan or Xdm.DayTimeDuration)
            return ValueTask.FromResult<object?>(0L);
        var ts = arg switch
        {
            string s => XmlConvert.ToTimeSpan(s),
            _ => XmlConvert.ToTimeSpan(arg.ToString()!)
        };
        return ValueTask.FromResult<object?>((long)(ts.Days / 365));
    }
}
