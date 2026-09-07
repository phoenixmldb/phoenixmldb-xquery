using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:document-uri() as xs:anyURI? (0-arg uses context item)</summary>
public sealed class DocumentUri0Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "document-uri");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalItem;
    public override IReadOnlyList<FunctionParameterDef> Parameters => [];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext ?? throw context.Error("XPDY0002", "Context item absent");
        var item = ctx.ContextItem;
        // Per spec: 0-arg document-uri() requires focus to be present (XPDY0002)
        if (item is null)
            throw context.Error("XPDY0002", "Context item is absent for fn:document-uri()");
        return new DocumentUriFunction().InvokeAsync([item], context);
    }
}
