using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:string($arg) as xs:string
/// </summary>
public sealed class StringFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "string");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalItem }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];

        // fn:string() requires a single item — sequences of 2+ are XPTY0004
        if (arg is object?[] seq && seq.Length > 1)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                "fn:string() requires zero or one item, got a sequence of " + seq.Length + " items");

        // fn:string() is not defined for function items, arrays, or maps — FOTY0014
        if (arg is List<object?>)
            throw context.Error("FOTY0014", "The string value of an array is not defined");
        if (arg is IDictionary<object, object?>)
            throw context.Error("FOTY0014", "The string value of a map is not defined");
        if (arg is Ast.XQueryFunction)
            throw context.Error("FOTY0014", "The string value of a function item is not defined");

        // For element/document nodes, compute string value by walking descendant text nodes
        if (arg is Xdm.Nodes.XdmElement elem2)
        {
            var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
            // The string value, not the typed value's string form: a validated xs:decimal
            // element holding "-0.0" has the string value "-0.0" (QT3 orderBy59).
            return ValueTask.FromResult<object?>(
                Execution.QueryExecutionContext.ComputeElementStringValue(elem2, nodeProvider));
        }
        if (arg is Xdm.Nodes.XdmDocument doc2)
        {
            var nodeProvider = (context as Execution.QueryExecutionContext)?.NodeProvider;
            return ValueTask.FromResult<object?>(
                Execution.QueryExecutionContext.ComputeDocumentStringValue(doc2, nodeProvider));
        }
        return ValueTask.FromResult<object?>(ConcatFunction.XQueryStringValue(arg));
    }
}
