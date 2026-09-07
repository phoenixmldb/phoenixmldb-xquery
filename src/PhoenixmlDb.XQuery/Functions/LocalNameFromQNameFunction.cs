using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:local-name-from-QName($arg) as xs:NCName?
/// </summary>
public sealed class LocalNameFromQNameFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "local-name-from-QName");
    public override XdmSequenceType ReturnType => XdmSequenceType.OptionalString;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = new XdmSequenceType { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg == null) return ValueTask.FromResult<object?>(null);
        if (arg is not QName qn)
            throw context.Error("XPTY0004", $"fn:local-name-from-QName() requires xs:QName, got {arg.GetType().Name}");
        return ValueTask.FromResult<object?>(new Xdm.XsTypedString(qn.LocalName, "NCName"));
    }
}
