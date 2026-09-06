using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:duplicate-values($input) as xs:anyAtomicType* (XPath 4.0)</summary>
public sealed class DuplicateValuesFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "duplicate-values");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
        => ValueTask.FromResult<object?>(ValueDistinctnessHelper.DuplicateValues(
            arguments[0], CollationHelper.GetDefaultComparison(context), context));
}
