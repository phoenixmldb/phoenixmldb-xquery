using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:format-date($value, $picture) as xs:string
/// </summary>
public sealed class FormatDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-date");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Date, Occurrence = Occurrence.ZeroOrOne } },
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
        long? extendedYear = null;
        var dt = arg switch
        {
            Xdm.XsDate xd => DateTimeFormatter.ExtractDateTimeOffset(xd, out extendedYear),
            DateOnly d => new DateTimeOffset(d, TimeOnly.MinValue, TimeSpan.Zero),
            Xdm.XsDateTime xdt => DateTimeFormatter.ExtractDateTimeOffset(xdt, out extendedYear),
            DateTimeOffset dto => dto,
            string s => DateTimeLexicalParse.ParseDateTimeLexical(s, "xs:date", context),
            _ => throw context.Error("XPTY0004", $"Expected xs:date, got {arg.GetType().Name}")
        };
        var hasTimezone = arg is Xdm.XsDate xd2 ? xd2.Timezone.HasValue : true;
        return ValueTask.FromResult<object?>(DateTimeFormatter.Format(dt, picture, hasDate: true, hasTime: false, extendedYear: extendedYear, hasTimezone: hasTimezone));
    }
}
