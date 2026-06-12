using PhoenixmlDb.Core;

namespace PhoenixmlDb.XQuery.Execution;

/// <summary>
/// The predicate an <see cref="IndexLookupOperator"/> resolves against an index:
/// either an exact value (<see cref="IndexEquality"/>) or a one- or two-sided range
/// (<see cref="IndexRange"/>). Passed as the opaque <c>object key</c> to the resolver.
/// </summary>
public abstract record IndexPredicate;

/// <summary>Exact-value lookup: <c>@attr = value</c>.</summary>
public sealed record IndexEquality(object Value) : IndexPredicate;

/// <summary>
/// Range lookup. Either bound may be null (open). Inclusivity flags apply only to a
/// present bound. E.g. <c>@p &lt; 100</c> -> <c>IndexRange(null, false, 100, false)</c>.
/// </summary>
public sealed record IndexRange(object? Lower, bool LowerInclusive, object? Upper, bool UpperInclusive) : IndexPredicate;

/// <summary>
/// A deferred comparand: the index lookup value comes from an external/in-scope
/// variable resolved at execution time, not a plan-time literal. May occupy the
/// <see cref="IndexEquality.Value"/> or a <see cref="IndexRange.Lower"/>/<see cref="IndexRange.Upper"/>
/// slot of an <see cref="IndexEquality"/>/<see cref="IndexRange"/>. The
/// <see cref="IndexLookupOperator"/> resolves it against the execution context before dispatch.
/// This is NOT itself an <see cref="IndexPredicate"/>.
/// </summary>
public sealed record VariableComparand(QName Name);
