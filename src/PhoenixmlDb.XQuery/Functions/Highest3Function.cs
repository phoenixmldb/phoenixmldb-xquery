using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:highest($input, $collation, $key) as item()*</summary>
public sealed class Highest3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "highest");
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
            NormaliseKey(arguments[2]), highest: true, context);

    // An explicitly-empty $key means "use the default", fn:data#1 — same as omitting it.
    internal static object? NormaliseKey(object? key)
        => key is object?[] { Length: 0 } ? null : key;
}
