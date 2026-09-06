using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Execution;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Helper to convert System.Text.Json elements to XDM maps, arrays, and atomic values.
/// </summary>
/// <summary>
/// Options for fn:parse-json.
/// </summary>
internal sealed class ParseJsonOptions
{
    public string Duplicates { get; set; } = "use-first";
    public bool Escape { get; set; }
    public XQueryFunction? Fallback { get; set; }

    /// <summary>
    /// Extracts parse-json options from the options map argument.
    /// </summary>
    internal static ParseJsonOptions FromMap(object? optionsArg)
    {
        var result = new ParseJsonOptions();
        if (optionsArg is not IDictionary<object, object?> map)
            return result;

        foreach (var kv in map)
        {
            var key = QueryExecutionContext.Atomize(kv.Key)?.ToString();
            var rawVal = kv.Value;
            // Don't atomize function items (they throw FOTY0013) — handled separately by "fallback"
            object? val = rawVal is XQueryFunction ? rawVal : QueryExecutionContext.Atomize(rawVal);
            switch (key)
            {
                case "duplicates":
                {
                    var s = val?.ToString();
                    if (s is not ("use-first" or "use-last" or "reject"))
                        throw new XQueryRuntimeException("FOJS0005",
                            $"Invalid value '{s}' for option 'duplicates' — " +
                            "must be 'use-first', 'use-last', or 'reject'");
                    result.Duplicates = s;
                    break;
                }
                case "escape":
                {
                    if (val is bool b)
                        result.Escape = b;
                    else
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Option 'escape' requires xs:boolean, got {val?.GetType().Name ?? "null"}");
                    break;
                }
                case "fallback":
                {
                    // The value must be a function item (arity 1).
                    if (rawVal is XQueryFunction fn)
                    {
                        if (fn.Arity != 1)
                            throw new XQueryRuntimeException("XPTY0004",
                                $"Option 'fallback' requires a function with arity 1, got arity {fn.Arity}");
                        result.Fallback = fn;
                    }
                    else
                    {
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Option 'fallback' requires a function item, got {rawVal?.GetType().Name ?? "null"}");
                    }
                    break;
                }
                case "liberal":
                {
                    if (val is not bool)
                        throw new XQueryRuntimeException("XPTY0004",
                            $"Option 'liberal' requires xs:boolean, got {val?.GetType().Name ?? "null"}");
                    break;
                }
                case "spec":
                    // Ignored per spec — retained for backwards compatibility
                    break;
                default:
                    // Unknown options are silently ignored per spec
                    break;
            }
        }

        if (result.Escape && result.Fallback != null)
            throw new XQueryRuntimeException("FOJS0005",
                "Options 'escape' and 'fallback' cannot both be specified");

        return result;
    }
}
