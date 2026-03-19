// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// Allow test assembly to access internal members
[assembly: InternalsVisibleTo("PhoenixmlDb.XQuery.Tests")]

// CA1852: Type can be sealed
// Suppressed for internal types that may be extended in tests or future versions.
[assembly: SuppressMessage("Performance", "CA1852:Seal internal types",
    Justification = "Internal types may be extended in tests")]

// CA2007: Consider calling ConfigureAwait on the awaited task
// Suppressed for library code - doesn't need to capture sync context.
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task",
    Justification = "Library code should not capture synchronization context")]

// CA1062: Validate arguments of public methods
// Suppressed for internal implementation methods.
[assembly: SuppressMessage("Design", "CA1062:Validate arguments of public methods",
    Justification = "Internal implementation methods with trusted callers")]

// CA1305: Specify IFormatProvider
// Suppressed for XQuery type parsing - uses invariant culture.
[assembly: SuppressMessage("Globalization", "CA1305:Specify IFormatProvider",
    Justification = "XQuery types use invariant culture")]

// CA1716: Identifiers should not match keywords
// Suppressed for XQuery standard function names.
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords",
    Justification = "XQuery standard function names")]

// CA1034: Nested types should not be visible
// Suppressed for AST expression types.
[assembly: SuppressMessage("Design", "CA1034:Nested types should not be visible",
    Justification = "AST expression grouping")]

// CA1812: Internal class is never instantiated
// Suppressed for visitor pattern implementations.
[assembly: SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Visitor pattern implementations")]

// CA1859: Use concrete types when possible for improved performance
// Suppressed for AST builder methods that return abstract expression types by design.
[assembly: SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance",
    Justification = "AST builders intentionally return abstract types for visitor pattern")]

// CA1861: Avoid constant arrays as arguments
// Suppressed for XQuery function parameter definitions.
[assembly: SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments",
    Justification = "Function parameter arrays are defined inline for clarity")]

// CA1822: Mark members as static
// Suppressed for visitor and builder methods that may need instance state in the future.
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Visitor/builder methods may need instance state")]

// CA1854: Prefer the 'IDictionary.TryGetValue(TKey, out TValue)' method
// Suppressed where ContainsKey + indexer pattern is clearer.
[assembly: SuppressMessage("Performance", "CA1854:Prefer the 'IDictionary.TryGetValue(TKey, out TValue)' method",
    Justification = "ContainsKey pattern preferred for clarity in some contexts")]

// CA1860: Avoid using 'Enumerable.Any()' extension method
// Suppressed for LINQ-style query code.
[assembly: SuppressMessage("Performance", "CA1860:Avoid using 'Enumerable.Any()' extension method",
    Justification = "LINQ Any() is appropriate for query expressions")]

// CA1864: Prefer the 'IDictionary.TryAdd(TKey, TValue)' method
// Suppressed where conditional add logic is clearer.
[assembly: SuppressMessage("Performance", "CA1864:Prefer the 'IDictionary.TryAdd(TKey, TValue)' method",
    Justification = "Explicit pattern preferred for clarity")]

// CA1868: Unnecessary call to 'Contains(item)'
// Suppressed where the pattern improves readability.
[assembly: SuppressMessage("Performance", "CA1868:Unnecessary call to 'Contains(item)'",
    Justification = "Contains check preferred for clarity")]

// CA2208: Instantiate argument exceptions correctly
// Suppressed for custom validation messages.
[assembly: SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly",
    Justification = "Custom validation messages")]

// CA1031: Do not catch general exception types
// Suppressed for query execution error handling.
[assembly: SuppressMessage("Design", "CA1031:Do not catch general exception types",
    Justification = "Query execution wraps exceptions appropriately")]

// CA1040: Avoid empty interfaces
// Suppressed for marker interfaces in AST.
[assembly: SuppressMessage("Design", "CA1040:Avoid empty interfaces",
    Justification = "Marker interfaces used for AST node categorization")]

// CA1720: Identifier contains type name
// Suppressed for XQuery type-related identifiers.
[assembly: SuppressMessage("Naming", "CA1720:Identifier contains type name",
    Justification = "XQuery type names match specification")]

// CA1308: Normalize strings to uppercase
// Suppressed for XQuery case-insensitive comparisons.
[assembly: SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase",
    Justification = "XQuery uses lowercase normalization")]

// CA1024: Use properties where appropriate
// Suppressed for factory methods.
[assembly: SuppressMessage("Design", "CA1024:Use properties where appropriate",
    Justification = "Factory methods return new instances")]

// CA1032: Implement standard exception constructors
// Suppressed for XQuery-specific exceptions that have domain-specific constructors.
[assembly: SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "XQuery exceptions have domain-specific constructors")]

// CA1002: Do not expose generic lists
// Suppressed for internal analysis methods that accumulate errors.
[assembly: SuppressMessage("Design", "CA1002:Do not expose generic lists",
    Justification = "Internal analysis methods use List for accumulation")]

// CA1054: URI parameters should not be strings
// Suppressed for XQuery namespace handling which uses string URIs.
[assembly: SuppressMessage("Design", "CA1054:URI parameters should not be strings",
    Justification = "XQuery uses string namespace URIs")]

// CA1056: URI properties should not be strings
// Suppressed for XQuery namespace handling which uses string URIs.
[assembly: SuppressMessage("Design", "CA1056:URI properties should not be strings",
    Justification = "XQuery uses string namespace URIs")]

// CA1307: Specify StringComparison
// Suppressed for XQuery string operations that use ordinal comparison.
[assembly: SuppressMessage("Globalization", "CA1307:Specify StringComparison",
    Justification = "XQuery string operations use ordinal comparison by default")]

// CA1715: Identifiers should have correct prefix
// Suppressed for ExecutionContext interface which matches XQuery naming.
[assembly: SuppressMessage("Naming", "CA1715:Identifiers should have correct prefix",
    Justification = "ExecutionContext matches XQuery terminology")]

// CA1805: Do not initialize unnecessarily
// Suppressed for explicit initialization that improves readability.
[assembly: SuppressMessage("Performance", "CA1805:Do not initialize unnecessarily",
    Justification = "Explicit initialization for clarity")]

// CA1819: Properties should not return arrays
// Suppressed for AST node properties.
[assembly: SuppressMessage("Performance", "CA1819:Properties should not return arrays",
    Justification = "AST properties use arrays for performance")]

// CA1826: Do not use Enumerable methods on indexable collections
// Suppressed for LINQ-style code patterns.
[assembly: SuppressMessage("Performance", "CA1826:Do not use Enumerable methods on indexable collections",
    Justification = "LINQ patterns used for consistency")]
