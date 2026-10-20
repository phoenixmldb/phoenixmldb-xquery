using System.Globalization;
using System.Xml;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:timezone-from-dateTime($arg as xs:dateTime?) as xs:dayTimeDuration?</summary>
public sealed class TimezoneFromDateTimeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "timezone-from-dateTime");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
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
        if (arg is Xdm.XsDateTime xdt)
            return ValueTask.FromResult<object?>(xdt.HasTimezone ? (object)xdt.Value.Offset : null);
        if (arg is DateTimeOffset dto)
            return ValueTask.FromResult<object?>((object)dto.Offset);
        var parsed = Xdm.XsDateTime.Parse(arg.ToString()!);
        return ValueTask.FromResult<object?>(parsed.HasTimezone ? (object)parsed.Value.Offset : null);
    }
}
