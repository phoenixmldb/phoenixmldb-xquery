using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:deep-equal($arg1, $arg2, $collation) as xs:boolean
/// </summary>
public sealed class DeepEqual3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "deep-equal");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "parameter1"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "parameter2"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Use XQueryStringValue to correctly atomize the collation argument,
        // including when it is a streaming node (object?[] sequence) rather than a raw string.
        var collationUri = ConcatFunction.XQueryStringValue(arguments[2]);
        var comparison = CollationHelper.GetStringComparison(collationUri);
        return DeepEqualFunction.DeepEqualWithComparison(arguments[0], arguments[1], comparison, context.NodeStore);
    }
}
