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

public sealed class PIConstructorOperator : PhysicalOperator
{
    public string? DirectTarget { get; init; }
    public PhysicalOperator? TargetOperator { get; init; }
    public required PhysicalOperator ContentOperator { get; init; }

    public override async IAsyncEnumerable<object?> ExecuteAsync(QueryExecutionContext context)
    {
        var store = context.NodeStore as INodeBuilder;

        // Determine target
        var target = DirectTarget;
        if (target == null && TargetOperator != null)
        {
            int count = 0;
            await foreach (var item in TargetOperator.ExecuteAsync(context))
            {
                if (count == 0)
                {
                    var atomized = context.AtomizeWithNodes(item);
                    // Per XQuery §3.7.3.5: the computed name must be xs:string, xs:untypedAtomic,
                    // or xs:NCName. Other types (xs:anyURI, xs:duration, etc.) raise XPTY0004.
                    if (atomized != null && atomized is not string
                        && atomized is not Xdm.XsUntypedAtomic)
                    {
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Processing instruction name must be xs:string or xs:untypedAtomic, got {atomized.GetType().Name}");
                    }
                    target = atomized?.ToString()?.Trim() ?? "";
                }
                count++;
                if (count > 1)
                    throw new XQueryRuntimeException("XPTY0004",
                        "Processing instruction name must be a single atomic value");
            }
            if (count == 0)
                throw new XQueryRuntimeException("XPTY0004",
                    "Processing instruction name cannot be an empty sequence");
        }
        target ??= "";
        // XQDY0064: PI target cannot be 'xml' (case-insensitive) per XQuery 3.1 §3.7.3.5
        if (target.Equals("xml", StringComparison.OrdinalIgnoreCase))
            throw new XQueryRuntimeException("XQDY0064", "Processing instruction target cannot be 'xml'");
        if (target.Contains(':'))
            throw new XQueryRuntimeException("XQDY0041",
                $"Processing instruction target '{target}' cannot contain ':'");
        try { System.Xml.XmlConvert.VerifyNCName(target); }
        catch
        {
            throw new XQueryRuntimeException("XQDY0041",
                $"Processing instruction target '{target}' is not a valid NCName");
        }

        // Evaluate content
        var sb = new StringBuilder();
        await foreach (var item in ContentOperator.ExecuteAsync(context))
        {
            if (item != null)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                var atomized = context.AtomizeWithNodes(item);
                sb.Append(Functions.ConcatFunction.XQueryStringValue(atomized));
            }
        }

        // Per XQuery §3.7.3.5: leading whitespace in computed PI content is stripped
        var value = sb.ToString().TrimStart();
        // XQDY0026: PI content must not contain '?>'
        if (value.Contains("?>"))
            throw new XQueryRuntimeException("XQDY0026",
                "Processing instruction content must not contain '?>'");

        var pi = new XdmProcessingInstruction
        {
            Id = store?.AllocateId() ?? new NodeId(0),
            Document = new DocumentId(0),
            Target = target,
            Value = value
        };
        store?.RegisterNode(pi);
        yield return pi;
    }
}
