using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:day-from-dateTime($arg as xs:dateTime?) as xs:integer?</summary>
public sealed class DayFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "day-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        // An untyped argument is cast to xs:dateTime, by the type's own parser, which
        // takes a year outside 1..9999; a .NET parse refused '-0012-12-03' (W3C XSLT
        // type-functions-0401).
        if (arg is Xdm.Nodes.XdmNode)
            arg = Execution.QueryExecutionContext.AtomizeTyped(arg, (context as Execution.QueryExecutionContext)?.NodeProvider);
        if (arg is null) return ValueTask.FromResult<object?>(null);
        if (arg is Xdm.XsUntypedAtomic or string)
            arg = Xdm.XsDateTime.Parse(arg is Xdm.XsUntypedAtomic untyped ? untyped.Value : (string)arg);
        var dt = ParseDateTime(arg);
        return ValueTask.FromResult<object?>((long)dt.Day);
    }

    private static DateTimeOffset ParseDateTime(object arg) => arg switch
    {
        Xdm.XsDateTime xdt => xdt.Value,
        Xdm.XsDate xd => new DateTimeOffset(xd.Date.Year, xd.Date.Month, xd.Date.Day, 0, 0, 0, xd.Timezone ?? TimeSpan.Zero),
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
        _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
    };
}
