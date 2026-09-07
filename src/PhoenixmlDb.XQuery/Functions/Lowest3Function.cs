using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:lowest($input, $collation, $key) as item()*</summary>
public sealed class Lowest3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "lowest");
    public override XdmSequenceType ReturnType => XdmSequenceType.ZeroOrMoreItems;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "input"), Type = XdmSequenceType.ZeroOrMoreItems },
        new() { Name = new QName(NamespaceId.None, "collation"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "key"), Type = new XdmSequenceType { ItemType = ItemType.Item, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
        => HighestLowestHelper.FindExtremeAsync(
            arguments[0], Sort2Function.ResolveCollation(arguments[1], context),
            Highest3Function.NormaliseKey(arguments[2]), highest: false, context);
}
