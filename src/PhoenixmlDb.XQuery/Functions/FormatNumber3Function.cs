using System.Globalization;
using System.Numerics;
using System.Text;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// fn:format-number($value, $picture, $decimal-format-name) as xs:string
/// </summary>
public sealed class FormatNumber3Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Fn, "format-number");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "value"), Type = new XdmSequenceType { ItemType = ItemType.Double, Occurrence = Occurrence.ZeroOrOne } },
        new() { Name = new QName(NamespaceId.None, "picture"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "decimal-format-name"), Type = XdmSequenceType.OptionalString }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments,
        Ast.ExecutionContext context)
    {
        // Handle empty sequence (null, empty array, empty list) as default format
        var rawArg = arguments.Count > 2 ? arguments[2] : null;
        if (rawArg is object?[] { Length: 0 } or List<object?> { Count: 0 }) rawArg = null;
        var formatName = rawArg?.ToString()?.Trim();
        // Resolve the format name: accept plain NCName, Q{uri}local, or prefixed "ex:name"
        // where ex is a statically known prefix.
        string? resolvedName = formatName;
        if (!string.IsNullOrEmpty(formatName) && context.DecimalFormats != null)
        {
            // Per XQuery 3.1 §4.10, decimal-format declarations are module-local. Inside a
            // library-module function body a plain NCName names that module's own format,
            // re-keyed as Q{moduleTargetNamespace}name, and it shadows a same-named format of
            // the importing module. The exact match came first, so the importer's "df001" won
            // (QT3 decimal-format-21).
            if (formatName.IndexOf(':') < 0
                && context is Execution.QueryExecutionContext { CurrentModuleNamespace: { } moduleNs }
                && context.DecimalFormats.ContainsKey($"Q{{{moduleNs}}}{formatName}"))
            {
                resolvedName = $"Q{{{moduleNs}}}{formatName}";
            }
            // Try exact match first
            else if (!context.DecimalFormats.ContainsKey(formatName))
            {
                // Try prefix resolution: "prefix:local" → "Q{uri}local"
                var colonIdx = formatName.IndexOf(':');
                if (colonIdx > 0 && colonIdx < formatName.Length - 1 && !formatName.StartsWith("Q{", StringComparison.Ordinal))
                {
                    var prefix = formatName[..colonIdx];
                    var local = formatName[(colonIdx + 1)..];
                    if (context is Execution.QueryExecutionContext qec
                        && qec.PrefixNamespaceBindings != null
                        && qec.PrefixNamespaceBindings.TryGetValue(prefix, out var uri))
                    {
                        var expanded = $"Q{{{uri}}}{local}";
                        if (context.DecimalFormats.ContainsKey(expanded))
                            resolvedName = expanded;
                    }
                }
            }
        }
        // FODF1280: if a non-null format name is supplied but no matching format exists,
        // raise an error. (A missing third arg or empty string uses the default format.)
        if (!string.IsNullOrEmpty(resolvedName))
        {
            if (context.DecimalFormats == null || !context.DecimalFormats.ContainsKey(resolvedName))
                throw new XQueryRuntimeException("FODF1280",
                    $"Decimal format '{formatName}' is not defined in the static context");
        }
        var df = FormatNumberFunction.GetDecimalFormat(context, resolvedName);
        var result = FormatNumberFunction.FormatNumberImpl(arguments[0], arguments[1]?.ToString() ?? "", df, context);
        return ValueTask.FromResult<object?>(result);
    }
}
