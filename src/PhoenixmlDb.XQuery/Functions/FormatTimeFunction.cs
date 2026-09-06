using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:format-time($value, $picture) as xs:string
/// </summary>
public sealed class FormatTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-time");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Time, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        // Function conversion rules (XPath 3.1 §3.1.5.2) CAST xs:untypedAtomic to the declared
        // parameter type. Atomizing a node yields XsUntypedAtomic — not string — so the
        // `string s =>` arm below missed it and every untyped node argument fell to the throw:
        //
        //     format-dateTime(@date, '[D] [MNn] [Y]')   ->  XPTY0004 ... got XsUntypedAtomic
        //
        // which is a plain attribute holding a dateTime, the ordinary case. Reported by
        // Martin Honnen against XSpec's format-xspec-report.xsl. Normalising to the lexical
        // form here routes it through the same parse the string arm uses.
        if (arg is Xdm.XsUntypedAtomic untypedArg) arg = untypedArg.Value;
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var picture = arguments[1]?.ToString() ?? "";
        var dt = arg switch
        {
            Xdm.XsTime xt => DateTimeFormatter.SafeTimeOffset(xt.Time, xt.Timezone ?? TimeSpan.Zero),
            TimeOnly t => new DateTimeOffset(DateOnly.MinValue, t, TimeSpan.Zero),
            TimeSpan ts => new DateTimeOffset(DateTime.MinValue.Add(ts)),
            Xdm.XsDateTime xdt => xdt.Value,
            DateTimeOffset dto => dto,
            string s => DateTimeLexicalParse.ParseTimeLexical(s, context),
            _ => throw context.Error("XPTY0004", $"Expected xs:time, got {arg.GetType().Name}")
        };
        var hasTimezone = arg is Xdm.XsTime xt2 ? xt2.Timezone.HasValue : true;
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: false, hasTime: true, hasTimezone: hasTimezone));
    }
}
