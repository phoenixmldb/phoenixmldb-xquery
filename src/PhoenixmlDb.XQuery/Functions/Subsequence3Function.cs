using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:subsequence($sourceSeq, $startingLoc, $length) as item()*
/// </summary>
public sealed class Subsequence3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "subsequence");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "sourceSeq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "startingLoc"), Type = XdmSequenceType.Double },
        new() { Name = new QName(NamespaceId.None, "length"), Type = XdmSequenceType.Double }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var source = arguments[0];
        // XPTY0004: $startingLoc and $length are xs:double (required) — empty sequence is a type error
        if (arguments[1] is null)
            throw new XQueryRuntimeException("XPTY0004",
                "An empty sequence is not allowed as the 2nd argument of subsequence()");
        if (arguments[2] is null)
            throw new XQueryRuntimeException("XPTY0004",
                "An empty sequence is not allowed as the 3rd argument of subsequence()");
        SequenceArgValidator.RequireNumeric(arguments[1], "subsequence", 2);
        SequenceArgValidator.RequireNumeric(arguments[2], "subsequence", 3);
        var startingLoc = QueryExecutionContext.ToDouble(arguments[1]);
        var length = QueryExecutionContext.ToDouble(arguments[2]);

        if (source == null)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        // Per XPath spec: if startingLoc or length is NaN, result is empty
        if (double.IsNaN(startingLoc) || double.IsNaN(length))
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var seq = source is object?[] arr ? arr : source is IEnumerable<object?> s ? s.ToArray() : new[] { source };

        // Per XPath spec: use double arithmetic for position/length to handle INF correctly
        // Items at position P (1-based) where round(startingLoc) <= P < round(startingLoc) + round(length)
        double roundedStart = Math.Round(startingLoc, MidpointRounding.AwayFromZero);
        double roundedLen = Math.Round(length, MidpointRounding.AwayFromZero);
        double endPos = roundedStart + roundedLen;

        // Convert to 0-based array indices, clamping to valid range
        int startIdx = roundedStart < 1 ? 0 : (roundedStart > seq.Length ? seq.Length : (int)roundedStart - 1);
        int endIdx = endPos < 1 ? 0 : (endPos > seq.Length + 1 ? seq.Length : (int)endPos - 1);

        if (startIdx >= endIdx)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var count = endIdx - startIdx;
        var result = new object?[count];
        Array.Copy(seq, startIdx, result, 0, count);
        return ValueTask.FromResult<object?>(result);
    }
}
