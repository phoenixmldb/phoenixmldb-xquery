using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:seconds-from-duration($arg as xs:duration?) as xs:decimal?</summary>
public sealed class SecondsFromDurationFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "seconds-from-duration");
    public override XdmSequenceType ReturnType => XdmSequenceType.Decimal;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsDuration dur)
        {
            var ts = dur.DayTime;
            return ValueTask.FromResult<object?>((decimal)ts.Seconds + (decimal)ts.Milliseconds / 1000m);
        }
        if (arg is Xdm.DayTimeDuration dtd)
            return ValueTask.FromResult<object?>(dtd.Seconds);
        // xs:yearMonthDuration has no seconds component
        if (arg is Xdm.YearMonthDuration)
            return ValueTask.FromResult<object?>(0m);
        var ts2 = arg switch
        {
            TimeSpan t => t,
            string s => XmlConvert.ToTimeSpan(s),
            _ => XmlConvert.ToTimeSpan(arg.ToString()!)
        };
        return ValueTask.FromResult<object?>((decimal)ts2.Seconds + (decimal)ts2.Milliseconds / 1000m);
    }
}

// ──────────────────────────────────────────────
// Timezone accessor functions
// ──────────────────────────────────────────────
