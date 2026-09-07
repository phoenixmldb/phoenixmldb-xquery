using System.Collections.Concurrent;
using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm.Nodes;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:normalize-unicode($arg, $normalizationForm) as xs:string
/// </summary>
public sealed class NormalizeUnicode2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "normalize-unicode");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "arg"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "normalizationForm"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        var str = arguments[0]?.ToString();
        if (str == null) return ValueTask.FromResult<object?>("");
        var formArg = arguments[1];
        if (formArg == null)
            throw context.Error("XPTY0004",
                "fn:normalize-unicode: normalization form cannot be an empty sequence");
        var form = (formArg.ToString() ?? "NFC").Trim().ToUpperInvariant();
        if (form.Length == 0)
            return ValueTask.FromResult<object?>(str); // empty string = no normalization
        var normForm = form switch
        {
            "NFC" => System.Text.NormalizationForm.FormC,
            "NFD" => System.Text.NormalizationForm.FormD,
            "NFKC" => System.Text.NormalizationForm.FormKC,
            "NFKD" => System.Text.NormalizationForm.FormKD,
            _ => throw new InvalidOperationException($"FOCH0003: Unsupported normalization form '{form}'")
        };
        return ValueTask.FromResult<object?>(str.Normalize(normForm));
    }
}
