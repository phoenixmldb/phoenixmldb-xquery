using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:normalize-unicode($arg) as xs:string
/// </summary>
public sealed class NormalizeUnicodeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "normalize-unicode");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var arg = arguments[0];
        if (arg != null)
            StringLengthFunction.RequireStringLike(arg, "normalize-unicode");
        var str = arg?.ToString() ?? "";
        return ValueTask.FromResult<object?>(str.Normalize(System.Text.NormalizationForm.FormC));
    }
}
