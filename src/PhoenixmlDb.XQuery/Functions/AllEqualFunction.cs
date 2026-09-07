using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:all-equal($input) as xs:boolean (XPath 4.0)</summary>
public sealed class AllEqualFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "all-equal");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
        => ValueTask.FromResult<object?>(ValueDistinctnessHelper.AllEqual(
            arguments[0], CollationHelper.GetDefaultComparison(context), context));
}
