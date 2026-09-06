using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.Xdm.Serialization;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;
using PhoenixmlDb.XQuery.Optimizer;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// XQuery 3.1/4.0: String constructor ``[content `{expr}` more]``.
/// Evaluates to a single string by concatenating literal parts and stringified expression results.
/// </summary>
public sealed class StringConstructorOperator : PhysicalOperator
{
    public required IReadOnlyList<StringConstructorPartOp> Parts { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var part in Parts)
        {
            if (part.LiteralValue != null)
            {
                sb.Append(part.LiteralValue);
            }
            else if (part.ExpressionOperator != null)
            {
                // XQuery §3.11.2: items in a string constructor interpolation are
                // atomized and separated by single spaces (same as attribute content).
                bool firstItem = true;
                await foreach (var item in part.ExpressionOperator.ExecuteAsync(context))
                {
                    if (item != null)
                    {
                        if (!firstItem)
                            sb.Append(' ');
                        sb.Append(context.AtomizeWithNodes(item)?.ToString() ?? "");
                        firstItem = false;
                    }
                }
            }
        }
        yield return sb.ToString();
    }
}
