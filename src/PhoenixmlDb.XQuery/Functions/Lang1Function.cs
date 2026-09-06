using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:lang($testlang as xs:string?) as xs:boolean (context item)</summary>
public sealed class Lang1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "lang");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "testlang"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var ctx = context as QueryExecutionContext ?? throw context.Error("XPDY0002", "Context item absent");
        var item = ctx.ContextItem ?? throw context.Error("XPDY0002", "Context item is absent");
        return new LangFunction().InvokeAsync([arguments[0], item], context);
    }
}
