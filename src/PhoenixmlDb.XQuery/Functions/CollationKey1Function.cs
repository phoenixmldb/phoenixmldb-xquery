using PhoenixmlDb.Core;
using PhoenixmlDb.Xdm;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;
using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:collation-key($value as xs:string, $collation as xs:string?) as xs:base64Binary
/// Returns a binary key for collation-based comparison (XPath 4.0).
/// </summary>
public sealed class CollationKey1Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "collation-key");
    public override XdmSequenceType ReturnType => XdmSequenceType.Item;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "value"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var arg = Execution.QueryExecutionContext.Atomize(arguments[0]);
        // Per spec: $value must be xs:string (xs:untypedAtomic and xs:anyURI promote to string, other types are XPTY0004)
        if (arg is Xdm.XsUntypedAtomic ua)
            arg = ua.Value;
        if (arg is Xdm.XsAnyUri anyUri)
            arg = anyUri.Value;
        if (arg is not string)
            throw context.Error("XPTY0004",
                $"First argument to fn:collation-key must be xs:string, got {arg?.GetType().Name ?? "empty sequence"}");
        var collation = CollationHelper.GetDefaultComparison(context) == StringComparison.Ordinal
            ? null : (context is Execution.QueryExecutionContext qec ? qec.DefaultCollation : null);
        return ValueTask.FromResult<object?>(ComputeCollationKey((string)arg, collation));
    }

    internal static Xdm.XdmValue ComputeCollationKey(string value, string? collationUri)
    {
        if (collationUri != null && collationUri.StartsWith("http://www.w3.org/2013/collation/UCA", StringComparison.Ordinal))
        {
            var (compareInfo, options) = CollationHelper.GetUcaCollation(collationUri);
            var sortKey = compareInfo.GetSortKey(value, options);
            var caseFirst = CollationHelper.GetCaseFirst(collationUri);
            if (caseFirst is "lower" or "upper")
            {
                // Generate a case-insensitive sort key for the primary/secondary levels,
                // then append a case-level suffix so that caseFirst ordering is applied
                // correctly in byte-wise comparison.
                var ciKey = compareInfo.GetSortKey(value, options | System.Globalization.CompareOptions.IgnoreCase);
                var keyData = ciKey.KeyData;
                var suffix = new byte[value.Length + 1];
                suffix[0] = 0x01; // separator
                for (int i = 0; i < value.Length; i++)
                    suffix[i + 1] = caseFirst == "lower"
                        ? (byte)(char.IsUpper(value[i]) ? 1 : 0)
                        : (byte)(char.IsLower(value[i]) ? 1 : 0);
                var combined = new byte[keyData.Length + suffix.Length];
                keyData.CopyTo(combined, 0);
                suffix.CopyTo(combined, keyData.Length);
                return Xdm.XdmValue.Base64Binary(combined);
            }
            return Xdm.XdmValue.Base64Binary(sortKey.KeyData);
        }

        var comparison = CollationHelper.GetStringComparison(collationUri);
        if (comparison == StringComparison.Ordinal)
        {
            // Codepoint collation: encode each Unicode codepoint as a 4-byte big-endian integer.
            // UTF-16 code units won't work because surrogate pairs (D800-DFFF) sort below
            // BMP chars E000-FFFF, breaking codepoint ordering for supplementary characters.
            return Xdm.XdmValue.Base64Binary(CodepointCollationKey(value));
        }
        if (comparison == StringComparison.OrdinalIgnoreCase)
        {
            var normalized = value.ToLowerInvariant();
            return Xdm.XdmValue.Base64Binary(CodepointCollationKey(normalized));
        }
        var sk = System.Globalization.CultureInfo.InvariantCulture.CompareInfo
            .GetSortKey(value, System.Globalization.CompareOptions.None);
        return Xdm.XdmValue.Base64Binary(sk.KeyData);
    }

    private static byte[] CodepointCollationKey(string value, Ast.ExecutionContext? context = null)
    {
        var codepoints = new List<int>(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            int cp = char.ConvertToUtf32(value, i);
            codepoints.Add(cp);
            if (char.IsHighSurrogate(value[i]))
                i++;
        }
        var bytes = new byte[codepoints.Count * 4];
        for (int i = 0; i < codepoints.Count; i++)
        {
            int cp = codepoints[i];
            bytes[i * 4]     = (byte)(cp >> 24);
            bytes[i * 4 + 1] = (byte)(cp >> 16);
            bytes[i * 4 + 2] = (byte)(cp >> 8);
            bytes[i * 4 + 3] = (byte)cp;
        }
        return bytes;
    }
}
