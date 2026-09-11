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
            // Try exact match first
            if (!context.DecimalFormats.ContainsKey(formatName))
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
                // Per XQuery 4.0 §4.18, decimal-format declarations are module-local.
                // When a plain NCName is used inside a library-module function body, try to
                // resolve it as Q{moduleTargetNamespace}name before falling back to bare name.
                // This allows the module's own "df001" to shadow a same-named format in the
                // importing module (QT3 decimal-format-21).
                if (resolvedName == formatName && colonIdx <= 0
                    && !formatName.StartsWith("Q{", StringComparison.Ordinal)
                    && context is Execution.QueryExecutionContext qecMod
                    && qecMod.CurrentModuleNamespace != null)
                {
                    var moduleExpanded = $"Q{{{qecMod.CurrentModuleNamespace}}}{formatName}";
                    if (context.DecimalFormats.ContainsKey(moduleExpanded))
                        resolvedName = moduleExpanded;
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
        var result = FormatNumberFunction.FormatNumberImpl(arguments[0], arguments[1]?.ToString() ?? "", df);
        return ValueTask.FromResult<object?>(result);
    }
}
