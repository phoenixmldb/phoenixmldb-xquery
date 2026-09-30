namespace PhoenixmlDb.Conformance.Tests.XQuery;

/// <summary>
/// Every element and attribute of the QT3 catalog vocabulary (the FOTS namespace), sorted into
/// what <see cref="XqtsTestRunner"/> implements, what it deliberately ignores, and what it does
/// not implement yet. <see cref="HarnessVocabularyTests"/> fails on anything in none of them.
/// </summary>
/// <remarks>
/// The harness grew one catalog feature at a time, and an unimplemented feature did not fail:
/// it silently scored correct engine output as wrong. On 2026-09-29/30 ten such gaps
/// accounted for over 400 QT3 failures (assert-permutation, normalize-space, decimal-format,
/// static-base-uri, source validation, ...). Listing every name makes the next gap loud, and
/// the gap list is the harness's own to-do list: an entry leaves it when the feature is done.
/// Attribute keys are "element@attribute".
/// </remarks>
public static class HarnessVocabulary
{
    /// <summary>Implemented by the runner.</summary>
    public static readonly IReadOnlySet<string> Handled = new HashSet<string>(StringComparer.Ordinal)
    {
        // Structure
        "catalog", "test-set", "test-set@name", "test-set@file", "test-case", "test-case@name",
        "test", "test@file", "result", "environment", "environment@name", "environment@ref",
        "dependency", "dependency@type", "dependency@value", "dependency@satisfied",
        // Environment
        "source", "source@file", "source@role", "source@uri", "source@validation",
        "resource", "resource@file", "resource@uri",
        "schema", "schema@file", "schema@uri", "schema@xsd-version",
        "module", "module@file", "module@uri",
        "namespace", "namespace@prefix", "namespace@uri",
        "param", "param@name", "param@select", "param@declared", "param@as",
        "collection", "collection@uri", "query", "context-item", "context-item@select",
        "decimal-format", "decimal-format@name", "decimal-format@decimal-separator",
        "decimal-format@grouping-separator", "decimal-format@digit", "decimal-format@zero-digit",
        "decimal-format@minus-sign", "decimal-format@percent", "decimal-format@per-mille",
        "decimal-format@infinity", "decimal-format@NaN", "decimal-format@pattern-separator",
        "decimal-format@exponent-separator",
        "static-base-uri", "static-base-uri@uri",
        // Assertions
        "all-of", "any-of", "not", "assert", "assert-count", "assert-deep-eq", "assert-empty",
        "assert-eq", "assert-false", "assert-true", "assert-permutation", "assert-type",
        "assert-string-value", "assert-string-value@normalize-space",
        "assert-xml", "assert-xml@file", "assert-xml@ignore-prefixes",
        "assert-serialization-error", "assert-serialization-error@code",
        "serialization-matches", "serialization-matches@flags",
        "error", "error@code",
    };

    /// <summary>Deliberately not read: documentation and provenance, which change no result.</summary>
    public static readonly IReadOnlyDictionary<string, string> Ignored = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["description"] = "prose",
        ["created"] = "provenance", ["created@by"] = "provenance", ["created@on"] = "provenance",
        ["modified"] = "provenance", ["modified@by"] = "provenance", ["modified@on"] = "provenance",
        ["modified@change"] = "provenance",
        ["link"] = "spec cross-reference", ["link@document"] = "spec cross-reference",
        ["link@idref"] = "spec cross-reference", ["link@section-number"] = "spec cross-reference",
        ["link@type"] = "spec cross-reference",
        ["test-case@covers"] = "coverage metadata", ["test-case@covers-30"] = "coverage metadata",
        ["test-set@covers"] = "coverage metadata", ["test-set@covers-30"] = "coverage metadata",
        ["catalog@test-suite"] = "suite identification", ["catalog@version"] = "suite identification",
        ["source@{http://www.w3.org/XML/1998/namespace}id"] = "anchor for cross-references",
        ["schema@{http://www.w3.org/XML/1998/namespace}id"] = "anchor for cross-references",
    };

    /// <summary>
    /// Catalog features the runner does not implement yet. Each silently mis-scores the cases
    /// that use it; the counts are failing cases on 2026-09-30.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> KnownGaps = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["query@uri"] = "the document URI of a collection member given as a query is not applied",
        ["collation"] = "environment collations are not declared (2 failing)",
        ["collation@uri"] = "environment collations are not declared",
        ["collation@default"] = "environment default collation is not declared",
        ["resource@encoding"] = "a resource's declared encoding is not applied",
        ["resource@media-type"] = "a resource's declared media type is not applied",
        ["module@location"] = "a module's location hint is not used when resolving it",
        ["schema@role"] = "role='import' (the driver imports the schema into the static context) is not applied",
    };
}
