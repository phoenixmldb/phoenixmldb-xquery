using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:day-from-date($arg as xs:date?) as xs:integer?</summary>
public sealed class DayFromDateFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "day-from-date");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = ParseDate(arg);
        return ValueTask.FromResult<object?>((long)dt.Day);
    }

    private static DateTimeOffset ParseDate(object arg) => arg switch
    {
        Xdm.XsDate xd => new DateTimeOffset(xd.Date.Year, xd.Date.Month, xd.Date.Day, 0, 0, 0, xd.Timezone ?? TimeSpan.Zero),
        Xdm.XsDateTime xdt => xdt.Value,
        DateOnly d => new DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero),
        DateTimeOffset dto => dto,
        DateTime dt => new DateTimeOffset(dt),
        string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
        _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
    };
}

// ──────────────────────────────────────────────
// DateTime component accessor functions
// ──────────────────────────────────────────────
