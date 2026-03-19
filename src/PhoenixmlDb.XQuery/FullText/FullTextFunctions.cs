using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;
using PhoenixmlDb.XQuery.Functions;

namespace PhoenixmlDb.XQuery.FullText;

/// <summary>
/// ft:score($node as node()) as xs:double
/// Returns the full-text relevance score of a node from the most recent contains-text evaluation.
/// Score is 0.0 (no match) to 1.0 (perfect match), normalized from BM25.
/// </summary>
public sealed class FtScoreFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "score");
    public override XdmSequenceType ReturnType => XdmSequenceType.Double;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "node"), Type = new() { ItemType = ItemType.Node, Occurrence = Occurrence.ExactlyOne } }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        // Score is stored in the execution context by the FtContainsOperator
        if (context is Execution.QueryExecutionContext qec)
        {
            var score = qec.GetFullTextScore(arguments[0]);
            return ValueTask.FromResult<object?>(score);
        }
        return ValueTask.FromResult<object?>(0.0);
    }
}

/// <summary>
/// ft:tokenize($text as xs:string?) as xs:string*
/// Tokenizes text using the default full-text analyzer.
/// Useful for debugging and understanding how text is analyzed.
/// </summary>
public sealed class FtTokenizeFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "tokenize");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "text"), Type = XdmSequenceType.OptionalString }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var text = arguments[0]?.ToString();
        if (string.IsNullOrEmpty(text)) return ValueTask.FromResult<object?>(Array.Empty<string>());

        var terms = FullTextEngine.Analyze(text);
        var result = terms.Select(t => t.Text).ToArray();
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// ft:tokenize($text as xs:string?, $language as xs:string) as xs:string*
/// Tokenizes text using a language-specific analyzer.
/// </summary>
public sealed class FtTokenize2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "tokenize");
    public override XdmSequenceType ReturnType => new() { ItemType = ItemType.String, Occurrence = Occurrence.ZeroOrMore };
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "text"), Type = XdmSequenceType.OptionalString },
        new() { Name = new QName(NamespaceId.None, "language"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var text = arguments[0]?.ToString();
        var language = arguments[1]?.ToString();
        if (string.IsNullOrEmpty(text)) return ValueTask.FromResult<object?>(Array.Empty<string>());

        var options = new FullTextAnalysisOptions { Language = language, Stemming = true };
        var terms = FullTextEngine.Analyze(text, options);
        var result = terms.Select(t => t.Text).ToArray();
        return ValueTask.FromResult<object?>(result);
    }
}

/// <summary>
/// ft:stem($term as xs:string) as xs:string
/// Returns the stemmed form of a word using the default analyzer.
/// </summary>
public sealed class FtStemFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "stem");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "term"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var term = arguments[0]?.ToString() ?? "";
        var terms = FullTextEngine.Analyze(term);
        return ValueTask.FromResult<object?>(terms.Count > 0 ? terms[0].Text : term);
    }
}

/// <summary>
/// ft:stem($term as xs:string, $language as xs:string) as xs:string
/// Returns the stemmed form using a language-specific stemmer.
/// </summary>
public sealed class FtStem2Function : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "stem");
    public override XdmSequenceType ReturnType => XdmSequenceType.String;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
    [
        new() { Name = new QName(NamespaceId.None, "term"), Type = XdmSequenceType.String },
        new() { Name = new QName(NamespaceId.None, "language"), Type = XdmSequenceType.String }
    ];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var term = arguments[0]?.ToString() ?? "";
        var language = arguments[1]?.ToString();
        var options = new FullTextAnalysisOptions { Language = language, Stemming = true };
        var terms = FullTextEngine.Analyze(term, options);
        return ValueTask.FromResult<object?>(terms.Count > 0 ? terms[0].Text : term);
    }
}

/// <summary>
/// ft:is-stop-word($word as xs:string) as xs:boolean
/// Tests if a word is a stop word in the default language.
/// </summary>
public sealed class FtIsStopWordFunction : XQueryFunction
{
    public override QName Name => new(FunctionNamespaces.Ft, "is-stop-word");
    public override XdmSequenceType ReturnType => XdmSequenceType.Boolean;
    public override IReadOnlyList<FunctionParameterDef> Parameters =>
        [new() { Name = new QName(NamespaceId.None, "word"), Type = XdmSequenceType.String }];

    public override ValueTask<object?> InvokeAsync(
        IReadOnlyList<object?> arguments, Ast.ExecutionContext context)
    {
        var word = arguments[0]?.ToString() ?? "";
        // Analyze the word — if it produces no tokens, it's a stop word
        var terms = FullTextEngine.Analyze(word, new FullTextAnalysisOptions { Stemming = false });
        return ValueTask.FromResult<object?>(terms.Count == 0);
    }
}

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
