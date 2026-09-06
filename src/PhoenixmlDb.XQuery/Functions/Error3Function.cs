using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:error($code, $description, $error-object) as none
/// </summary>
public sealed class Error3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "error");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.Zero };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "code"), Type = new() { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "description"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "error-object"), Type = XdmSequenceType.ZeroOrMoreItems }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var code = arguments[0];
        var description = arguments[1]?.ToString() ?? "Error raised by fn:error";
        var errorObject = arguments.Count > 2 ? arguments[2] : null;
        var (errorCode, errorNs, errorPrefix) = ErrorFunction.ExtractErrorQName(code);
        throw new XQueryException(errorCode, description) { ErrorNamespaceUri = errorNs, ErrorPrefix = errorPrefix, ErrorValue = errorObject };
    }
}
