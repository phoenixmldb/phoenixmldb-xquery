using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:seconds-from-dateTime($arg as xs:dateTime?) as xs:decimal?</summary>
public sealed class SecondsFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "seconds-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.Decimal;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        var dt = arg switch
        {
            DateTimeOffset dto => dto,
            string s => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture),
            _ => DateTimeOffset.Parse(arg.ToString()!, CultureInfo.InvariantCulture)
        };
        return ValueTask.FromResult<object?>((decimal)dt.Second + (decimal)dt.Millisecond / 1000m);
    }
}

// ──────────────────────────────────────────────
// Time component accessor functions
// ──────────────────────────────────────────────
