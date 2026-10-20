using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:local-name-from-QName($arg) as xs:NCName?
/// </summary>
public sealed class LocalNameFromQNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "local-name-from-QName");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // The parameter is xs:QName?, so a node argument is atomized: a validated xs:QName
        // element supplies its typed value. A node was rejected outright.
        var arg = arguments[0] is Xdm.Nodes.XdmNode node
            ? Execution.QueryExecutionContext.AtomizeTyped(node, (context as Execution.QueryExecutionContext)?.NodeProvider)
            : arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(null);
        // An untyped value is not cast to xs:QName, a namespace-sensitive type: XPTY0117
        // (XPath 3.1 §3.1.5.2). It was reported as XPTY0004 (QT3 FunctionCall-018).
        if (arg is Xdm.XsUntypedAtomic)
            throw context.Error("XPTY0117", $"fn:local-name-from-QName(): an xs:untypedAtomic value is not cast to xs:QName");
        if (arg is not QName qn)
            throw context.Error("XPTY0004", $"fn:local-name-from-QName() requires xs:QName, got {arg.GetType().Name}");
        return ValueTask.FromResult<object?>(new Xdm.XsTypedString(qn.LocalName, "NCName"));
    }
}
