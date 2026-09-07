using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:error($code as xs:QName?) as none
/// </summary>
public sealed class Error1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "error");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.Zero };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "code"), Type = new() { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne } }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var code = arguments[0];
        var (errorCode, errorNs, errorPrefix) = ErrorFunction.ExtractErrorQName(code);
        throw new XQueryException(errorCode, $"Error raised by fn:error: {errorCode}") { ErrorNamespaceUri = errorNs, ErrorPrefix = errorPrefix };
    }
}
