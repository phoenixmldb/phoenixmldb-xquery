using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:static-base-uri() as xs:anyURI?
/// </summary>
public sealed class StaticBaseUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "static-base-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var uri = context.StaticBaseUri;
        return ValueTask.FromResult<object?>(uri != null ? new XsAnyUri(uri) : null);
    }
}
