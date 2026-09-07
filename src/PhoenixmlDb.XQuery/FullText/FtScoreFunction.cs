using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// ft:score($node as node()) as xs:double
/// Returns the full-text relevance score of a node from the most recent contains-text evaluation.
/// Score is 0.0 (no match) to 1.0 (perfect match), normalized from BM25.
/// </summary>
public sealed class FtScoreFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "score");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "node"), Type = new() { ItemType = ItemType.Node, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        // Score is stored in the execution context by the FtContainsOperator
        if (context is Execution.QueryExecutionContext qec)
        {
            var score = qec.GetFullTextScore(arguments[0]);
            return ValueTask.FromResult<object?>(score);
        }
        return ValueTask.FromResult<object?>(0.0);
    }
}
