using PhoenixmlDb.Core;
using PhoenixmlDb.XQuery.Ast;

namespace PhoenixmlDb.XQuery.Functions;

/// <summary>
/// Holds a cached snapshot of DateTimeOffset.Now for stable current-dateTime/date/time functions.
/// Per XPath F&amp;O §16.6, these functions must return the same value throughout a single execution scope.
/// Create one instance per transformation and share it across all three current-* function instances.
/// </summary>
public sealed class CurrentDateTimeSnapshot
{
    private DateTimeOffset? _now;

    public DateTimeOffset Now => _now ??= DateTimeOffset.Now;

    /// <summary>Resets the cached value for a new execution scope.</summary>
    public void Reset() => _now = null;
}
