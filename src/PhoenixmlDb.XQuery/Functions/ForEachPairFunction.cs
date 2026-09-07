using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:for-each-pair($seq1, $seq2, $action) as item()*
/// </summary>
public sealed class ForEachPairFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "for-each-pair");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "seq1"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "seq2"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "f"), Type = new() { ItemType = ItemType.Function, Occurrence = Occurrence.ExactlyOne } }
    ];

    public override async ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var seq1 = SequenceHelper.Flatten(arguments[0]);
        var seq2 = SequenceHelper.Flatten(arguments[1]);
        var callable = arguments[2];
        // Maps and arrays have arity 1, not 2 — XPTY0004
        if (callable is IDictionary<object, object?> || callable is List<object?>)
            throw new XQueryRuntimeException("XPTY0004",
                "fn:for-each-pair requires a function of arity 2, maps and arrays have arity 1");
        var func = callable as XQueryFunction
            ?? throw new XQueryRuntimeException("XPTY0004",
                "Third argument to fn:for-each-pair must be a function");
        if (func.Parameters.Count != 2)
            throw new XQueryRuntimeException("XPTY0004",
                $"fn:for-each-pair requires a function of arity 2, got arity {func.Parameters.Count}");

        var results = new List<object?>();
        var len = Math.Min(seq1.Count, seq2.Count);
        for (int i = 0; i < len; i++)
        {
            var result = await func.InvokeAsync([seq1[i], seq2[i]], context);
            if (result is IEnumerable<object?> resultSeq)
            {
                foreach (var r in resultSeq) results.Add(r);
            }
            else if (result != null)
                results.Add(result);
        }
        return results.ToArray();
    }
}
