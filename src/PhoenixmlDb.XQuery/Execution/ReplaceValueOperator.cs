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
/// Replace a node's value.
/// Collects a <see cref="Ast.ReplaceValuePrimitive"/> into the context's PUL.
/// </summary>
public sealed class ReplaceValueOperator : PhysicalOperator
{
    public required PhysicalOperator Target { get; init; }
    public required PhysicalOperator Value { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        // XQUF 3.0 §5.3.2. The new value as the content of a text node constructor: every item
        // atomized, the strings joined with a space. Only the first item was taken, unatomized,
        // and a node became the text of its .NET object.
        var text = new StringBuilder();
        var items = new List<object?>();
        await foreach (var item in Value.ExecuteGuarded(context))
            UpdateTargets.Flatten(item, items);
        foreach (var item in items)
        {
            if (text.Length > 0)
                StringLengthLimit.Append(context, text, " ", "A replace value expression");
            StringLengthLimit.Append(context, text,
                Functions.ConcatFunction.XQueryStringValue(context.AtomizeWithNodes(item)), "A replace value expression");
        }
        var value = text.ToString();

        var targetNode = await UpdateTargets.SingleAsync(Target, context, "replace value expression", "XUTY0008",
            "element, attribute, text, comment or processing instruction",
            static n => n is XdmElement or XdmAttribute or XdmText or XdmComment or XdmProcessingInstruction).ConfigureAwait(false);
        if (targetNode is XdmComment && (value.Contains("--", StringComparison.Ordinal) || value.EndsWith('-')))
            throw new XQueryRuntimeException("XQDY0072",
                "replace value expression: a comment cannot contain '--' or end with '-'.");
        if (targetNode is XdmProcessingInstruction && value.Contains("?>", StringComparison.Ordinal))
            throw new XQueryRuntimeException("XQDY0026",
                "replace value expression: a processing instruction cannot contain '?>'.");

        context.PendingUpdates.AddReplaceValue(targetNode, value);

        yield break;
    }
}
