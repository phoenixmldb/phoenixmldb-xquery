using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:substring($sourceString, $start, $length) as xs:string
/// </summary>
public sealed class Substring3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "substring");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "sourceString"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "start"), Type = XdmSequenceType.Double },
        new() { Name = new QName(NamespaceId.None, "length"), Type = XdmSequenceType.Double }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var source = QueryExecutionContext.ToString(arguments[0]);
        var start = QueryExecutionContext.ToDouble(arguments[1]);
        var length = QueryExecutionContext.ToDouble(arguments[2]);

        // XPath rounds .5 towards positive infinity (not banker's rounding)
        var roundedStart = Math.Round(start, MidpointRounding.AwayFromZero);
        var roundedLength = Math.Round(length, MidpointRounding.AwayFromZero);

        // Per XPath spec: return codepoints at 1-based position p where
        // round(start) <= p < round(start) + round(length)
        // NaN comparisons are always false, so NaN naturally produces ""
        if (double.IsNaN(roundedStart) || double.IsNaN(roundedLength) ||
            double.IsNaN(roundedStart + roundedLength))
            return ValueTask.FromResult<object?>("");

        int startPos = double.IsNegativeInfinity(roundedStart) ? int.MinValue : (int)roundedStart;
        double endPosD = roundedStart + roundedLength;
        int endPos = double.IsPositiveInfinity(endPosD) ? int.MaxValue :
                     double.IsNegativeInfinity(endPosD) ? int.MinValue : (int)endPosD;

        int clampedStart = Math.Max(1, startPos);
        int clampedLength = endPos > int.MaxValue - clampedStart ? int.MaxValue : Math.Max(0, endPos - clampedStart);

        // XPath substring uses codepoint-based positions, not UTF-16 char positions.
        return ValueTask.FromResult<object?>(SubstringHelper.SubstringByCodepoints(source, clampedStart, clampedLength));
    }
}
