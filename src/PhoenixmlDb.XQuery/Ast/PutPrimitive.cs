namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// upd:put (XQUF 3.0 §8.1.12): store the node, as it is after the other updates of the list, at
/// <see cref="Uri"/>. Made by <c>fn:put</c>, which has authorised the write.
/// </summary>
internal sealed class PutPrimitive : UpdatePrimitive
{
    /// <summary>The absolute URI the resource policy allowed (for a file, its canonical path).</summary>
    public required Uri Uri { get; init; }

    /// <summary>The <c>output:serialization-parameters</c> element, or <c>null</c>.</summary>
    public object? Parameters { get; init; }
}
