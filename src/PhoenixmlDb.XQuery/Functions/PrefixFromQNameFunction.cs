using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:prefix-from-QName($arg) as xs:NCName?
/// </summary>
public sealed class PrefixFromQNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "prefix-from-QName");
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
        if (arg is not QName qn)
            throw context.Error("XPTY0004", $"fn:prefix-from-QName() requires xs:QName, got {arg.GetType().Name}");
        // xs:NCName, not a plain string (QT3 fn-prefix-from-qname-21/22, xquery#83).
        return ValueTask.FromResult<object?>(string.IsNullOrEmpty(qn.Prefix) ? null : (object?)new Xdm.XsTypedString(qn.Prefix, "NCName"));
    }
}
