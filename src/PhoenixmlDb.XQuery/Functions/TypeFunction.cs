using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:type($item as item()) as record(name, namespace, kind) — returns type info (XPath 4.0).
/// </summary>
public sealed class TypeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "type");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Map, Occurrence = Occurrence.ExactlyOne };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "item"), Type = XdmSequenceType.Item }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var item = arguments[0];
        var result = new OrderedXdmMap(XdmMapKeyComparer.Instance);

        // Delegates so fn:type and the engine's diagnostics cannot drift apart — they
        // answer the same question. Before this, fn:type(xs:byte(1)) returned the CLR name
        // "XsTypedInteger" with kind "item", because tagged subtypes matched no arm.
        var (kind, typeName) = XdmShape.TypeOf(item);

        result["kind"] = kind;
        result["name"] = typeName;
        return ValueTask.FromResult<object?>(result);
    }
}
