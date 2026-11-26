using PhoenixmlDb.Xdm.Nodes;

namespace PhoenixmlDb.XQuery.Ast;

/// <summary>
/// upd:put (XQUF 3.0 §8.1.12): store <see cref="UpdatePrimitive.Target"/>, a document or element
/// node, as it is after the other updates of the list, at <see cref="Uri"/>. Made by <c>fn:put</c>.
/// </summary>
/// <remarks>
/// <para>
/// It is written when the list is applied (<see cref="Execution.PendingUpdateApplicator.ApplyUpdates"/>),
/// after every check and after the other updates of the list, and never while the query runs. The
/// write is authorised then by the context's <see cref="Security.ResourcePolicy"/>
/// (<see cref="Security.ResourceAccessKind.WriteDocument"/>); without a policy it is refused
/// (<c>phx:XUDY9001</c>).
/// </para>
/// <para>
/// When the policy has a <see cref="Security.ResourcePolicy.ResourceResolver"/>, its
/// <see cref="Security.IResourceResolver.OpenResultDocument"/> is given the URI as text (see
/// <see cref="Uri"/>). A writer it returns receives the serialized node in one write and is
/// disposed; <c>null</c> means the host does not take it, and the library writes the file.
/// Without a resolver, the library writes the file, and <c>fn:put</c> also authorised it when it
/// was called, so a refused write stops the query before any update is applied.
/// </para>
/// </remarks>
public sealed class PutPrimitive : UpdatePrimitive
{
    /// <summary>
    /// The absolute URI <c>fn:put</c> named, resolved against the static base URI when it is
    /// relative (RFC 3986). When <c>fn:put</c> authorised a file write (no host resolver), the
    /// canonical file URI the policy returned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Its <see cref="System.Uri.OriginalString"/> is the text that
    /// <see cref="Security.IResourceResolver.OpenResultDocument"/> is given and that the policy
    /// authorises when the list is applied: the URI as the query wrote it (leading and trailing
    /// white space removed), with no normalisation by <see cref="System.Uri"/>. Its
    /// <see cref="System.Uri.AbsoluteUri"/> can differ: it decodes the escapes of unreserved
    /// characters (<c>%2E</c> to <c>.</c>, <c>%41</c> to <c>A</c>) and, for a URI with an authority
    /// (<c>phoenixml://h/c/../d.xml</c>), removes dot segments. A relative URI is given as RFC 3986
    /// resolves it (dot segments removed, escapes as written); a path (<c>/data/out.xml</c>) as its
    /// <c>file:</c> URI.
    /// </para>
    /// <para>
    /// Two puts whose URIs have the same <see cref="System.Uri.AbsoluteUri"/> are XUDY0031: they
    /// name one resource.
    /// </para>
    /// </remarks>
    public required Uri Uri { get; init; }

    /// <summary>
    /// The <c>output:serialization-parameters</c> element <c>fn:put#3</c> was given, or <c>null</c>
    /// (<c>fn:put#2</c>, or an empty sequence). <c>fn:put</c> validated it when it was called, with
    /// the rules of <c>fn:serialize</c> (XPTY0004 for another item, SEPM0016 and SEPM0017 for an
    /// invalid parameter), so an invalid one stops the query before any update is applied.
    /// </summary>
    /// <remarks>
    /// The node is serialized with these parameters for a host writer and for a file alike. The
    /// library gives a host writer characters, so <c>encoding</c> and <c>byte-order-mark</c> are not
    /// applied to it: the writer decides the bytes. A file is written in UTF-8 without a byte order
    /// mark.
    /// </remarks>
    public XdmElement? Parameters { get; init; }

    /// <summary>
    /// The output method of the put: <c>"xml"</c>, <c>"xhtml"</c>, <c>"html"</c>, <c>"text"</c>,
    /// <c>"json"</c> or <c>"adaptive"</c>, as <see cref="Parameters"/> gives it, and <c>"xml"</c>
    /// when they give none (<c>fn:put#2</c> too). A host can refuse a method it does not store.
    /// </summary>
    public string Method { get; init; } = "xml";

    /// <summary><see cref="Parameters"/> as <c>fn:put</c> read them when it was called, for the serialization.</summary>
    internal IDictionary<object, object?>? ParsedParameters { get; init; }
}
