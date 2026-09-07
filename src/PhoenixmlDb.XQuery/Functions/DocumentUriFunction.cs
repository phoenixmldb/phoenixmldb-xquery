using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:document-uri($arg) as xs:anyURI?
/// </summary>
public sealed class DocumentUriFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "document-uri");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.AnyUri, Occurrence = Occurrence.ZeroOrOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg is XdmDocument doc)
        {
            // Per spec, fn:document-uri returns xs:anyURI, not xs:string
            return ValueTask.FromResult<object?>(doc.DocumentUri != null ? new Xdm.XsAnyUri(doc.DocumentUri) : null);
        }

        return ValueTask.FromResult<object?>(null);
    }
}
