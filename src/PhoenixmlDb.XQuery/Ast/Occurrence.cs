namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// Occurrence indicator for sequence types, controlling the cardinality (how many items are allowed).
/// </summary>
/// <remarks>
/// Corresponds to the XQuery occurrence indicators: no suffix means exactly one,
/// <c>?</c> means zero or one, <c>*</c> means zero or more, and <c>+</c> means one or more.
/// For example, <c>xs:string?</c> is <see cref="ItemType.String"/> with <see cref="ZeroOrOne"/>.
/// </remarks>
/// <seealso cref="XdmSequenceType"/>
public enum Occurrence
{
    /// <summary>The empty sequence — <c>empty-sequence()</c>.</summary>
    Zero,
    /// <summary>Exactly one item — no occurrence indicator.</summary>
    ExactlyOne,
    /// <summary>Zero or one item — the <c>?</c> indicator.</summary>
    ZeroOrOne,
    /// <summary>Zero or more items — the <c>*</c> indicator.</summary>
    ZeroOrMore,
    /// <summary>One or more items — the <c>+</c> indicator.</summary>
    OneOrMore
}
