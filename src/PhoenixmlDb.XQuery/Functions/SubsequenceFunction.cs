using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:subsequence($sourceSeq, $startingLoc) as item()*
/// </summary>
public sealed class SubsequenceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "subsequence");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "sourceSeq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "startingLoc"), Type = XdmSequenceType.Double }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var source = arguments[0];
        // XPTY0004: $startingLoc is xs:double (required) — empty sequence is a type error
        if (arguments[1] is null)
            throw new XQueryRuntimeException("XPTY0004",
                "An empty sequence is not allowed as the 2nd argument of subsequence()");
        var startingLoc = QueryExecutionContext.ToDouble(arguments[1]);

        // Per XPath spec: if startingLoc is NaN, result is empty
        if (source == null || double.IsNaN(startingLoc))
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        var seq = source is object?[] arr ? arr : source is IEnumerable<object?> s ? s.ToArray() : new[] { source };

        if (double.IsNegativeInfinity(startingLoc))
            return ValueTask.FromResult<object?>(seq);
        if (double.IsPositiveInfinity(startingLoc))
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        // XPath uses 1-based indexing; round .5 towards positive infinity
        var startIndex = (int)Math.Round(startingLoc, MidpointRounding.AwayFromZero) - 1;
        if (startIndex < 0) startIndex = 0;
        if (startIndex >= seq.Length)
            return ValueTask.FromResult<object?>(Array.Empty<object>());

        if (startIndex == 0)
            return ValueTask.FromResult<object?>(seq);

        var result = new object?[seq.Length - startIndex];
        Array.Copy(seq, startIndex, result, 0, result.Length);
        return ValueTask.FromResult<object?>(result);
    }
}
