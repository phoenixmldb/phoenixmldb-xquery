using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace PhoenixmlDb.XQuery;

/// <summary>
/// XSD 1.1 conditional inclusion (§4.2.2, the <c>vc:</c> versioning namespace), applied as the
/// schema processor this engine uses: System.Xml's, which implements XSD 1.0. An element whose
/// <c>vc:minVersion</c> is above 1.0, or whose <c>vc:maxVersion</c> is 1.0 or below, is not part
/// of the schema for such a processor and is removed before loading.
/// </summary>
/// <remarks>
/// Schemas written for both versions mark their 1.1-only parts this way, typically
/// <c>&lt;xs:assert test="…" vc:minVersion="1.1"/&gt;</c>. Loading them unprocessed failed outright
/// ("The 'xs:assert' element is not supported in this context"), so the whole schema was lost for
/// a construct the schema itself says a 1.0 processor must skip (W3C stream-107 and siblings).
/// </remarks>
internal static class XsdVersionControl
{
    private const string VcNamespace = "http://www.w3.org/2007/XMLSchema-versioning";
    private const decimal ProcessorVersion = 1.0m;

    /// <summary>True when the text declares the versioning namespace at all; otherwise it is left untouched.</summary>
    internal static bool Mentions(string xsd)
        => xsd.Contains(VcNamespace, StringComparison.Ordinal)
           || Xsd11BuiltIns.Keys.Any(k => xsd.Contains(k, StringComparison.Ordinal));

    private const string XsdNamespace = "http://www.w3.org/2001/XMLSchema";

    /// <summary>
    /// Built-in types XSD 1.1 added (and XPath 2.0 had first) that System.Xml's XSD 1.0 processor
    /// does not know, each with the 1.0 type it restricts. A schema referring to one failed to load
    /// at all ("Type 'xs:dayTimeDuration' is not declared"), losing every declaration in it for a
    /// single attribute (QT3 validateexpr-28..42, validate-*, cbcl-validateexpr-*: 48 cases on one
    /// shared schema). The reference is rewritten to the base type, so the schema loads and the
    /// value still validates lexically; a node validated against it is annotated with the base
    /// type rather than the 1.1 one.
    /// </summary>
    private static readonly Dictionary<string, string> Xsd11BuiltIns = new(StringComparer.Ordinal)
    {
        ["dayTimeDuration"] = "duration",
        ["yearMonthDuration"] = "duration",
        ["dateTimeStamp"] = "dateTime",
    };

    internal static string Apply(string xsd)
    {
        var doc = XDocument.Parse(xsd, LoadOptions.PreserveWhitespace);
        var minName = XName.Get("minVersion", VcNamespace);
        var maxName = XName.Get("maxVersion", VcNamespace);
        foreach (var element in doc.Descendants())
            foreach (var attribute in element.Attributes())
                if (attribute.Name.LocalName is "type" or "base" or "itemType" or "memberTypes"
                    && attribute.Name.Namespace == XNamespace.None)
                    attribute.Value = string.Join(' ', attribute.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Select(qn => MapBuiltIn(qn, element)));
        foreach (var element in doc.Descendants().ToList())
        {
            if (element.Parent == null && element != doc.Root) continue; // already removed with an ancestor
            if (Version(element.Attribute(minName)) is { } min && min > ProcessorVersion
                || Version(element.Attribute(maxName)) is { } max && max <= ProcessorVersion)
                element.Remove();
        }
        return doc.Declaration is null ? doc.ToString(SaveOptions.DisableFormatting) : doc.Declaration + doc.ToString(SaveOptions.DisableFormatting);
    }

    private static string MapBuiltIn(string qname, XElement scope)
    {
        var colon = qname.IndexOf(':', StringComparison.Ordinal);
        var prefix = colon < 0 ? "" : qname[..colon];
        var local = colon < 0 ? qname : qname[(colon + 1)..];
        var ns = colon < 0 ? scope.GetDefaultNamespace() : scope.GetNamespaceOfPrefix(prefix);
        return ns?.NamespaceName == XsdNamespace && Xsd11BuiltIns.TryGetValue(local, out var baseType)
            ? (colon < 0 ? baseType : prefix + ":" + baseType)
            : qname;
    }

    private static decimal? Version(XAttribute? attribute)
        => attribute != null && decimal.TryParse(attribute.Value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    /// <summary>
    /// Resolves schema documents — the ones loaded by location and everything they include or
    /// import — through <see cref="Apply"/>.
    /// </summary>
    internal sealed class Resolver : XmlUrlResolver
    {
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var entity = base.GetEntity(absoluteUri, role, ofObjectToReturn);
            if (entity is not Stream stream)
                return entity;
            string text;
            using (var reader = new StreamReader(stream))
                text = reader.ReadToEnd();
            if (Mentions(text))
                text = Apply(text);
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        }
    }
}
