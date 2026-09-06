using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:adjust-dateTime-to-timezone($arg as xs:dateTime?) as xs:dateTime?</summary>
public sealed class AdjustDateTimeToTimezoneFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "adjust-dateTime-to-timezone");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is null) return ValueTask.FromResult<object?>(null);
        return ValueTask.FromResult<object?>(AdjustDateTime(arg, DateTimeOffset.Now.Offset));
    }

    internal static object? AdjustDateTime(object? arg, TimeSpan? newTz, Ast.ExecutionContext? context = null)
    {
        if (arg is Xdm.XsDateTime xdt)
        {
            if (newTz is null)
                return new Xdm.XsDateTime(new DateTimeOffset(xdt.Value.DateTime, TimeSpan.Zero), false);
            if (!xdt.HasTimezone)
                return new Xdm.XsDateTime(new DateTimeOffset(xdt.Value.DateTime, newTz.Value), true);
            var adjusted = xdt.Value.ToOffset(newTz.Value);
            return new Xdm.XsDateTime(adjusted, true);
        }
        var dt = DateTimeHelper.ParseDateTimeOffset(arg!);
        if (newTz is null)
            return new Xdm.XsDateTime(new DateTimeOffset(dt.DateTime, TimeSpan.Zero), false);
        return new Xdm.XsDateTime(dt.ToOffset(newTz.Value), true);
    }
}
