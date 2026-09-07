using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:substring($sourceString, $start) as xs:string
/// </summary>
public sealed class SubstringFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "substring");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "sourceString"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "start"), Type = XdmSequenceType.Double }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var source = QueryExecutionContext.ToString(arguments[0]);
        var start = QueryExecutionContext.ToDouble(arguments[1]);

        // XPath rounds .5 towards positive infinity (not banker's rounding)
        var roundedStart = Math.Round(start, MidpointRounding.AwayFromZero);
        if (double.IsNaN(roundedStart) || double.IsPositiveInfinity(roundedStart))
            return ValueTask.FromResult<object?>("");

        int startPos = double.IsNegativeInfinity(roundedStart) ? 1 : (int)roundedStart;
        int clampedStart = Math.Max(1, startPos);

        // XPath substring uses codepoint-based positions, not UTF-16 char positions.
        return ValueTask.FromResult<object?>(SubstringHelper.SubstringByCodepoints(source, clampedStart, int.MaxValue));
    }
}
