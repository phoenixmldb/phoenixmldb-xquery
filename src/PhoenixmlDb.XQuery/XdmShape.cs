namespace PhoenixmlDb.XQuery;

/// <summary>
/// The one place that states how an XDM <em>sequence</em> and an XDM <em>array</em> are told
/// apart at runtime, and the safe operations over that distinction.
/// </summary>
/// <remarks>
/// <para>
/// The convention: a SEQUENCE is <c>object?[]</c>, an ARRAY is <c>List&lt;object?&gt;</c>, and a
/// single item is itself. Nothing in the type system says so — both are just containers of
/// <c>object?</c>, they have the same shape, and the compiler cannot help. Every consumer has
/// to remember, and five did not:
/// </para>
/// <list type="bullet">
///   <item>the <c>xquery</c> CLI's serializer printed <c>12</c> for <c>[1,2]</c></item>
///   <item><c>SerializeItemAdaptive</c> gave sequences array brackets</item>
///   <item>the QT3 runner flattened arrays when binding <c>$result</c> — 56 failures</item>
///   <item><c>xsl:array</c>'s spread would have flattened a nested array member</item>
///   <item><c>xsl:array</c> handed the finished array on AS a sequence</item>
/// </list>
/// <para>
/// The last is the one to learn from. It produced the RIGHT answer for the obvious case —
/// <c>&lt;xsl:array select="1 to 5"/&gt;</c> still printed <c>[1,2,3,4,5]</c> — and broke only
/// at the edges, where a one-member array collapsed to its member and <c>composite="yes"</c>
/// silently became a no-op. A fix verified against the reported case alone would have shipped
/// looking complete.
/// </para>
/// <para>
/// <b>This is mitigation, not the fix.</b> The fix is a wrapper type so the compiler enforces
/// the distinction; that is a refactor of ~90 sites across two shipping engines and has not
/// been done. What this class does is give the three decisions that actually went wrong —
/// "is this one item or many?", "what do I iterate?", "how do I hand this on?" — names that
/// say which answer you are asking for.
/// </para>
/// </remarks>
public static class XdmShape
{
    /// <summary>True if the value is an XDM array. An array is ONE item.</summary>
    public static bool IsArray(object? value) => value is List<object?>;

    /// <summary>True if the value is a multi-item sequence representation.</summary>
    public static bool IsSequence(object? value) => value is object?[];

    /// <summary>
    /// The items of <paramref name="value"/> viewed as a SEQUENCE: a sequence yields its items,
    /// and everything else — including an array — is a single item.
    /// </summary>
    /// <remarks>
    /// Use when iterating "the things in this value". An array must NOT be spread here: it is
    /// one item whose members are its own business.
    /// </remarks>
    public static IReadOnlyList<object?> SequenceItems(object? value) => value switch
    {
        null => [],
        object?[] seq => seq,
        _ => new[] { value },       // an array lands here deliberately: one item
    };

    /// <summary>
    /// The members of <paramref name="value"/> when it is an array; <c>null</c> when it is not.
    /// </summary>
    /// <remarks>Use when you mean "look inside this array", never to iterate a sequence.</remarks>
    public static IReadOnlyList<object?>? ArrayMembers(object? value)
        => value as List<object?>;

    /// <summary>
    /// Packages <paramref name="items"/> as a SEQUENCE value: empty stays empty, one item
    /// unwraps to itself, and more become <c>object?[]</c>.
    /// </summary>
    /// <remarks>
    /// The unwrap matters: a one-item sequence IS its item in XDM, and leaving it wrapped makes
    /// downstream shape tests disagree with the spec.
    /// </remarks>
    public static object? AsSequence(IReadOnlyList<object?> items) => items.Count switch
    {
        0 => Array.Empty<object?>(),
        1 => items[0],
        _ => items.ToArray(),
    };

    /// <summary>
    /// Packages <paramref name="members"/> as an ARRAY value — always one item, never unwrapped.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="AsSequence"/>, and the operation <c>xsl:array</c> got wrong
    /// by calling <c>.ToArray()</c>: that produced a sequence of the members instead of an array.
    /// </remarks>
    public static object? AsArray(IEnumerable<object?> members) => new List<object?>(members);
}
