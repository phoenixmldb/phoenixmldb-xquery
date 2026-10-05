using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:error($code, $description) as none
/// </summary>
public sealed class ErrorFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "error");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.Item, Occurrence = Occurrence.Zero };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "code"), Type = new() { ItemType = ItemType.QName, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "description"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var code = arguments[0];
        var description = arguments[1]?.ToString() ?? "Error raised by fn:error";
        var (errorCode, errorNs, errorPrefix) = ExtractErrorQName(code);
        throw new XQueryException(errorCode, description) { ErrorNamespaceUri = errorNs, ErrorPrefix = errorPrefix };
    }

    internal static (string localName, string? namespaceUri, string? prefix) ExtractErrorQName(object? code)
    {
        if (code is QName qn)
        {
            var ns = qn.ExpandedNamespace ?? qn.RuntimeNamespace;
            return (qn.LocalName, string.IsNullOrEmpty(ns) ? null : ns, qn.Prefix);
        }
        // $code is xs:QName? (F&O 3.1 §3.1.1). Anything else was turned into a code by ToString(),
        // so fn:error('Wrong Argument Type') raised an error called "Wrong Argument Type" instead
        // of the type error it is (QT3 fn-error-3).
        if (code is not null)
            throw new XQueryException("XPTY0004",
                $"The first argument of fn:error must be an xs:QName, not {(code is string ? "xs:string" : code.GetType().Name)}");
        return ("FOER0000", null, null);
    }
}
