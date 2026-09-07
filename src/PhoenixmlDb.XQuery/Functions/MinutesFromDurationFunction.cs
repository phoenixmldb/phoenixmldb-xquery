using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:minutes-from-duration($arg as xs:duration?) as xs:integer?</summary>
public sealed class MinutesFromDurationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "minutes-from-duration");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDuration dur)
            return ValueTask.FromResult<object?>((long)dur.DayTime.Minutes);
        if (arg is Xdm.DayTimeDuration dtd)
            return ValueTask.FromResult<object?>((long)dtd.Minutes);
        // xs:yearMonthDuration has no minutes component
        if (arg is Xdm.YearMonthDuration)
            return ValueTask.FromResult<object?>(0L);
        var ts = arg switch
        {
            TimeSpan t => t,
            string s => XmlConvert.ToTimeSpan(s),
            _ => XmlConvert.ToTimeSpan(arg.ToString()!)
        };
        return ValueTask.FromResult<object?>((long)ts.Minutes);
    }
}
