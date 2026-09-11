using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:minutes-from-time($arg as xs:time?) as xs:integer?</summary>
public sealed class MinutesFromTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "minutes-from-time");
    public override XdmSequenceType ReturnType => XdmSequenceType.Integer;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

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
        return ValueTask.FromResult<object?>((long)dt.Minute);
    }
}
