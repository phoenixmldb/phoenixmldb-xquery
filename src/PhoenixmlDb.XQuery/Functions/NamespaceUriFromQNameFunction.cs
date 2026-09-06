using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:namespace-uri-from-QName($arg) as xs:anyURI?
/// </summary>
public sealed class NamespaceUriFromQNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "namespace-uri-from-QName");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(null);
        if (arg is not QName qn)
            throw context.Error("XPTY0004", $"fn:namespace-uri-from-QName() requires xs:QName, got {arg.GetType().Name}");

        var nsUri = qn.ResolvedNamespace;
        if (nsUri != null)
            return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(nsUri));
        var qec = context as PhoenixmlDb.XQuery.Execution.QueryExecutionContext;
        return ValueTask.FromResult<object?>(new PhoenixmlDb.Xdm.XsAnyUri(NamespaceUriFunction.ResolveNsId(qn.Namespace, qec)));
    }
}
