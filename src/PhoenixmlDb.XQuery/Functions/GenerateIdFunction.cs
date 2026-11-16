using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:generate-id($arg as node()?) as xs:string</summary>
public sealed class GenerateIdFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "generate-id");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var node = arguments[0];
        // Normalize empty sequence (object[]{}) to null per fn:generate-id($arg as node()?) signature
        if (node is object?[] { Length: 0 }) node = null;
        if (node == null) return ValueTask.FromResult<object?>("");
        // XPTY0004: argument must be a node
        if (node is not XdmNode)
            throw new Execution.XQueryRuntimeException("XPTY0004",
                $"fn:generate-id expects a node, got {node.GetType().Name}");
        return ValueTask.FromResult<object?>(IdFor((XdmNode)node));
    }

    // A node with no id (a parentless node made on its own) has no identity key; it gets a
    // number of its own the first time, kept for as long as the node lives.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<XdmNode, string> s_unkeyed = new();
    private static long s_nextUnkeyed;

    /// <summary>
    /// The id of a node: the same for the same node and different for different nodes, and an
    /// NCName (F&amp;O 3.1 §14.6.4). It is made from the key node identity uses
    /// (<see cref="XdmNode.DocumentOrderKey"/>, which `is` compares). It was the node object's
    /// hash code, which is not unique: different nodes could share an id, and two objects for
    /// one node got different ones.
    /// </summary>
    internal static string IdFor(XdmNode node)
    {
        if (node.Id.Value != 0)
            return $"N{node.TreeOrdinal:X}n{node.Id.Value:X}";
        return s_unkeyed.GetValue(node, static _ => $"U{System.Threading.Interlocked.Increment(ref s_nextUnkeyed):X}");
    }
}
