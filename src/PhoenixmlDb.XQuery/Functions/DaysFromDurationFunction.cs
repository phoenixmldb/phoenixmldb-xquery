using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:days-from-duration($arg as xs:duration?) as xs:integer?</summary>
public sealed class DaysFromDurationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "days-from-duration");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>((long)dur.DayTime.Days);
        if (arg is Xdm.DayTimeDuration dtd)
            return ValueTask.FromResult<object?>(dtd.Days);
        // xs:yearMonthDuration has no day component
        if (arg is Xdm.YearMonthDuration)
            return ValueTask.FromResult<object?>(0L);
        var ts = arg switch
        {
            TimeSpan t => t,
            string s => XmlConvert.ToTimeSpan(s),
            _ => XmlConvert.ToTimeSpan(arg.ToString()!)
        };
        return ValueTask.FromResult<object?>((long)ts.Days);
    }
}
