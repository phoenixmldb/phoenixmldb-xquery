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
        if (form == "FULLY-NORMALIZED")
            return ValueTask.FromResult<object?>(FullyNormalize(str));
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

    /// <summary>
    /// Fully-normalized text (Character Model for the World Wide Web: Normalization, §2.2): NFC,
    /// and not beginning with a composing character, which a space is put before (QT3
    /// cbcl-fn-normalize-unicode-006: U+09BE gives " &#x9BE;"). The form was refused with FOCH0003.
    /// </summary>
    internal static string FullyNormalize(string str)
    {
        var nfc = str.Normalize(System.Text.NormalizationForm.FormC);
        return nfc.Length > 0 && IsComposingCharacter(char.ConvertToUtf32(nfc, 0)) ? " " + nfc : nfc;
    }

    /// <summary>
    /// A composing character has a non-zero canonical combining class or is the second
    /// character of the canonical decomposition of a primary composite.
    /// </summary>
    private static bool IsComposingCharacter(int codepoint)
        => HasNonZeroCombiningClass(codepoint) || s_secondOfComposite.Value.Contains(codepoint);

    /// <summary>
    /// .NET does not expose the combining class, but canonical ordering shows it: a mark of class
    /// 1 to 239 is moved before U+0345 (class 240) and a starter (class 0) is not.
    /// </summary>
    private static bool HasNonZeroCombiningClass(int codepoint)
    {
        if (codepoint == 0x345)
            return true;
        var c = char.ConvertFromUtf32(codepoint);
        var decomposed = c.Normalize(System.Text.NormalizationForm.FormD);
        return ("\u0345" + c).Normalize(System.Text.NormalizationForm.FormD) != "\u0345" + decomposed;
    }

    private static readonly Lazy<HashSet<int>> s_secondOfComposite = new(() =>
    {
        // Each primary composite: a character with a canonical decomposition that composes
        // back to it (composition exclusions do not). Its decomposition's last character is
        // the second of its canonical mapping, the first being the part that may decompose
        // further.
        var set = new HashSet<int>();
        for (var cp = 0; cp <= 0x10FFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF)
                continue;
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(cp) == System.Globalization.UnicodeCategory.OtherNotAssigned)
                continue;
            var c = char.ConvertFromUtf32(cp);
            var d = c.Normalize(System.Text.NormalizationForm.FormD);
            if (d == c || d.Normalize(System.Text.NormalizationForm.FormC) != c)
                continue;
            var last = char.IsLowSurrogate(d[^1]) ? char.ConvertToUtf32(d[^2], d[^1]) : d[^1];
            set.Add(last);
        }
        return set;
    });
}
