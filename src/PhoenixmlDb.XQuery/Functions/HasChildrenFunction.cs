using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>fn:has-children($node as node()?) as xs:boolean</summary>
public sealed class HasChildrenFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "has-children");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "node"), Type = XdmSequenceType.OptionalNode }];

    public override ValueTask<object?> InvokeAsync(IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var node = arguments[0];
        if (node == null) return ValueTask.FromResult<object?>(false);
        return ValueTask.FromResult<object?>(HasChildren(node, context));
    }

    internal static bool HasChildren(object? node, Ast.ExecutionContext? context = null) => node switch
    {
        XdmElement e => e.Children != null && e.Children.Count > 0,
        XdmDocument d => d.Children != null && d.Children.Count > 0,
        XdmNode _ => false, // other node types (text, comment, PI, attribute, namespace) have no children
        _ => throw context.Error("XPTY0004", "Argument to fn:has-children is not a node")
    };
}
