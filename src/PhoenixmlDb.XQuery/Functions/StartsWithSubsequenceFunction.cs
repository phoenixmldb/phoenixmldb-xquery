using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:starts-with-subsequence($seq as item()*, $subseq as item()*) as xs:boolean (XPath 4.0).
/// </summary>
public sealed class StartsWithSubsequenceFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "starts-with-subsequence");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "subseq"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var seq = arguments[0] is object?[] a1 ? a1 : (arguments[0] != null ? new[] { arguments[0] } : Array.Empty<object?>());
        var sub = arguments[1] is object?[] a2 ? a2 : (arguments[1] != null ? new[] { arguments[1] } : Array.Empty<object?>());

        if (sub.Length == 0) return ValueTask.FromResult<object?>(true);
        if (sub.Length > seq.Length) return ValueTask.FromResult<object?>(false);

        for (var j = 0; j < sub.Length; j++)
        {
            if (!Equals(QueryExecutionContext.Atomize(seq[j])?.ToString(),
                        QueryExecutionContext.Atomize(sub[j])?.ToString()))
                return ValueTask.FromResult<object?>(false);
        }
        return ValueTask.FromResult<object?>(true);
    }
}
