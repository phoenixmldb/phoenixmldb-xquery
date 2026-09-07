using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// ft:thesaurus-lookup($term as xs:string, $relationship as xs:string?) as xs:string*
/// Looks up synonyms/related terms in a thesaurus.
/// Basic built-in thesaurus with common synonyms.
/// </summary>
public sealed class FtThesaurusLookupFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "thesaurus-lookup");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "term"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "relationship"), Type = XdmSequenceType.OptionalString }
    ];
    public override bool IsVariadic => true;
    public override int MinArity => 1;
    public override int MaxArity => 2;

    // Basic built-in thesaurus — extensible via external files
    private static readonly Dictionary<string, string[]> _synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["big"] = ["large", "huge", "enormous", "vast"],
        ["small"] = ["little", "tiny", "minute", "compact"],
        ["fast"] = ["quick", "rapid", "swift", "speedy"],
        ["slow"] = ["gradual", "unhurried", "leisurely"],
        ["good"] = ["excellent", "fine", "great", "superior"],
        ["bad"] = ["poor", "inferior", "terrible", "awful"],
        ["happy"] = ["glad", "joyful", "pleased", "content"],
        ["sad"] = ["unhappy", "sorrowful", "melancholy", "gloomy"],
        ["begin"] = ["start", "commence", "initiate"],
        ["end"] = ["finish", "conclude", "terminate", "complete"],
        ["create"] = ["make", "build", "construct", "produce"],
        ["delete"] = ["remove", "erase", "destroy", "eliminate"],
        ["find"] = ["search", "locate", "discover", "detect"],
        ["change"] = ["modify", "alter", "adjust", "transform"],
    };

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var term = arguments[0]?.ToString()?.ToLowerInvariant() ?? "";
        // var relationship = arguments.Count > 1 ? arguments[1]?.ToString() : null;

        if (_synonyms.TryGetValue(term, out var synonyms))
            return ValueTask.FromResult<object?>(synonyms);

        // Also check reverse lookup (if "large" is entered, find "big" and return its synonyms)
        foreach (var (key, values) in _synonyms)
        {
            if (values.Contains(term, StringComparer.OrdinalIgnoreCase))
            {
                var results = new List<string> { key };
                results.AddRange(values.Where(v => !string.Equals(v, term, StringComparison.OrdinalIgnoreCase)));
                return ValueTask.FromResult<object?>(results.ToArray());
            }
        }

        return ValueTask.FromResult<object?>(Array.Empty<string>());
    }
}
